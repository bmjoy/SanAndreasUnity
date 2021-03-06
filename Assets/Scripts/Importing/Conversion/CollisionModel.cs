using SanAndreasUnity.Importing.Collision;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.Profiling;

namespace SanAndreasUnity.Importing.Conversion
{
    public class CollisionModel
    {
        private static UnityEngine.Vector3 Convert(Vector3 vec)
        {
            return new UnityEngine.Vector3(vec.X, vec.Z, vec.Y);
        }

        private static Mesh Convert(IEnumerable<Face> faces, IEnumerable<Vertex> vertices)
        {
			Profiler.BeginSample ("Convert mesh");

            var mesh = new Mesh
            {
                vertices = vertices.Select(x => Convert(x.Position)).ToArray(),
                subMeshCount = 1
            };

            var indices = faces.SelectMany(x => x.GetIndices()).ToArray();

            /*This will work too
            var l = indices.ToList ();
			var a = indices.Reverse ().ToArray (); 
			for (int i = 0; i < indices.Reverse ().ToArray ().Length; i++)
				l.Add (a[i]);
			indices = l.ToArray();

			mesh.SetTriangles(indices, 0);*/
            
            int[] triangles = new int[indices.Length * 2];
            indices.CopyTo(triangles, 0);

            for (int i = 0; i < indices.Length; i += 3)
            {
                int temp = indices[i + 0];
                indices[i + 0] = indices[i + 1];
                indices[i + 1] = temp;
            }
            indices.CopyTo(triangles, indices.Length);
            mesh.SetIndices(triangles, MeshTopology.Triangles, 0);

			Profiler.EndSample ();

            return mesh;
        }

        private static Mesh Convert(FaceGroup group, IEnumerable<Face> faces, IEnumerable<Vertex> vertices)
        {
            return Convert(faces.Skip(group.StartFace).Take(1 + group.EndFace - group.StartFace), vertices);
        }

        private static GameObject _sTemplateParent;

        private static readonly Dictionary<string, CollisionModel> _sLoaded
            = new Dictionary<string, CollisionModel>();

        public static void Load(string name, Transform destParent, bool forceConvex = false)
        {
            Load(name, null, destParent, forceConvex);
        }

        public static void Load(CollisionFile file, Transform destParent, bool forceConvex = false)
        {
            Load(file.Name, file, destParent, forceConvex);
        }

        private static void Load(string name, CollisionFile file, Transform destParent, bool forceConvex)
        {
            CollisionModel col;

            if (_sLoaded.ContainsKey(name))
            {
                col = _sLoaded[name];
                if (col == null) return;

                col.Spawn(destParent, forceConvex);
                return;
            }

            file = file ?? CollisionFile.FromName(name);
            if (file == null || (file.Flags & Flags.NotEmpty) != Flags.NotEmpty)
            {
                _sLoaded.Add(name, null);
                return;
            }

            col = new CollisionModel(file);
            _sLoaded.Add(name, col);

            col.Spawn(destParent, forceConvex);
        }

		public static void LoadAsync(string name, CollisionFile file, Transform destParent, bool forceConvex, System.Action onFinish)
		{
			// load collision file asyncly, and when it's ready just call the other function

			if (file != null)
			{
				// collision file already loaded
				// just call other function

				Utilities.F.RunExceptionSafe( () => Load(file, destParent, forceConvex) );
				onFinish ();
				return;
			}

			// load collision file asyncly
			CollisionFile.FromNameAsync (name, (cf) => {
				// loading finished
				// call other function
				if(cf != null)
					Utilities.F.RunExceptionSafe( () => Load( cf, destParent, forceConvex ) );
				onFinish ();
			});

		}


        private readonly GameObject _template;
        private readonly Dictionary<SurfaceFlags, Transform> _flagGroups;

        private void Add<TCollider>(Surface surface, Action<TCollider> setup)
            where TCollider : Collider
        {
			Profiler.BeginSample ("Add<" + typeof(TCollider).Name + ">");

            if (!_flagGroups.ContainsKey(surface.Flags))
            {
                var group = new GameObject(string.Format("Group {0}", (int)surface.Flags));
                group.transform.SetParent(_template.transform);

                _flagGroups.Add(surface.Flags, group.transform);
            }

            var type = typeof(TCollider);
            var obj = new GameObject(type.Name, type);
            obj.transform.SetParent(_flagGroups[surface.Flags]);

            setup(obj.GetComponent<TCollider>());

			Profiler.EndSample ();
        }

        private CollisionModel(CollisionFile file)
        {
			Profiler.BeginSample ("CollisionModel()");

            if (_sTemplateParent == null)
            {
                _sTemplateParent = new GameObject("Collision Templates");
                _sTemplateParent.SetActive(false);
            }

            _template = new GameObject(file.Name);
            _template.transform.SetParent(_sTemplateParent.transform);

            _flagGroups = new Dictionary<SurfaceFlags, Transform>();

            foreach (var box in file.Boxes)
            {
                Add<BoxCollider>(box.Surface, x =>
                {
                    var min = Convert(box.Min);
                    var max = Convert(box.Max);

                    x.center = (min + max) * .5f;
                    x.size = (max - min);
                });
            }

            foreach (var sphere in file.Spheres)
            {
                Add<SphereCollider>(sphere.Surface, x =>
                {
                    x.center = Convert(sphere.Center);
                    x.radius = sphere.Radius;
                });
            }

            if (file.FaceGroups.Length > 0)
            {
                foreach (var group in file.FaceGroups)
                {
                    Add<MeshCollider>(file.Faces[group.StartFace].Surface, x =>
                    {
                        x.sharedMesh = Convert(group, file.Faces, file.Vertices);
                    });
                }
            }
            else if (file.Faces.Length > 0)
            {
                Add<MeshCollider>(file.Faces[0].Surface, x =>
                {
                    x.sharedMesh = Convert(file.Faces, file.Vertices);
                });
            }

            // TODO: MeshCollider


			Profiler.EndSample ();
        }

        public void Spawn(Transform destParent, bool forceConvex)
        {
            var clone = Object.Instantiate(_template.gameObject);

            clone.name = "Collision";
            clone.transform.SetParent(destParent, false);

            //	Debug.Log ("Setting parent (" + destParent.name + ") for " + clone.name);

            if (!forceConvex) return;

			Profiler.BeginSample ("Adjust colliders");

            foreach (var collider in clone.GetComponentsInChildren<Collider>())
            {
                var meshCollider = collider as MeshCollider;

                collider.gameObject.layer = 2;

                if (meshCollider != null)
                {
                    meshCollider.convex = true;
                }
            }

			Profiler.EndSample ();
        }
    }
}
