using System.Collections.Generic;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [DisallowMultipleComponent]
    public sealed class EquipmentTransformReference : MonoBehaviour
    {
        private bool _captured;
        private Vector3 _worldScale;
        private Quaternion _localRotation;
        private Vector3 _baseOffsetInMeters;

        private sealed class SkinMesh
        {
            public Mesh Source, Clone;
            public Vector3 Center;
        }

        private bool _skinsCached, _fitApplied;
        private Vector3 _fitPos, _fitEuler, _fitScale;
        private SkinnedMeshRenderer[] _skins;
        private Mesh[] _skinSources;
        private readonly List<SkinMesh> _skinMeshes = new List<SkinMesh>();

        public static bool IsSkinned(Transform item)
        {
            if (item == null) return false;
            foreach (var skin in item.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh != null && skin.sharedMesh.bindposes.Length > 0) return true;
            }

            return false;
        }

        public static void ClearSkinnedFit(Transform item)
        {
            item?.GetComponent<EquipmentTransformReference>()?.ClearSkinnedFit();
        }

        public static EquipmentTransformReference Get(Transform item)
        {
            var reference = item.GetComponent<EquipmentTransformReference>();
            if (reference == null) reference = item.gameObject.AddComponent<EquipmentTransformReference>();
            if (!reference._captured)
            {
                reference._worldScale = item.lossyScale;
                reference._localRotation = item.localRotation;
                reference._baseOffsetInMeters = item.parent != null
                    ? Quaternion.Inverse(item.parent.rotation) * (item.position - item.parent.position)
                    : item.position;
                reference._captured = true;
            }

            return reference;
        }

        public void SetScale(float ratio)
        {
            SetScale(ratio, Vector3.one);
        }

        public void SetScale(float ratio, Vector3 multiplier)
        {
            if (ratio <= 0 || float.IsNaN(ratio) || float.IsInfinity(ratio)) return;
            var parentMatrix = transform.parent != null ? transform.parent.localToWorldMatrix : Matrix4x4.identity;
            if (AttachmentMath.TryLocalScale(AttachmentTransforms.Matrix(parentMatrix),
                    AttachmentTransforms.Rotation(transform.localRotation),
                    AttachmentTransforms.Vector(Vector3.Scale(_worldScale * ratio, multiplier)),
                    out var scale))
                transform.localScale = AttachmentTransforms.Vector(scale);
        }

        public void SetRotationOffset(Vector3 offset)
        {
            transform.localRotation = _localRotation * Quaternion.Euler(offset);
        }

        public void SetPositionOffset(Vector3 offsetInMeters, float ratio)
        {
            var parent = transform.parent;
            var matrix = parent != null ? parent.localToWorldMatrix : Matrix4x4.identity;
            var rotation = parent != null ? parent.rotation : Quaternion.identity;
            if (AttachmentMath.TryMapItemOffset(AttachmentTransforms.Matrix(matrix),
                    AttachmentTransforms.Rotation(rotation),
                    AttachmentTransforms.Vector(_baseOffsetInMeters + offsetInMeters),
                    ratio,
                    out var position))
                transform.localPosition = AttachmentTransforms.Vector(position);
        }

        // Capes and most chest and leg pieces are skinned onto the body. Cloth skins with the
        // authored bind poses and the current vertices. Scaling those poses (01470cc) left the
        // vertices where they were, and the cloth solver stretched the sheet into the sky.
        // Vertices move instead, and the poses are copied unchanged. <1, 1, 1> and a zero
        // offset put the original mesh back, which is the cape at its normal size.
        public void ApplySkinnedFit(Vector3 position, Vector3 euler, Vector3 scale)
        {
            CacheSkins();
            if (_skinMeshes.Count == 0) return;
            if (_fitApplied && _fitPos == position && _fitEuler == euler && _fitScale == scale && ClonesAssigned())
                return;
            _fitPos = position;
            _fitEuler = euler;
            _fitScale = scale;
            _fitApplied = true;
            if (position == Vector3.zero && euler == Vector3.zero && scale == Vector3.one)
            {
                AssignSources();
                DestroyClones();
                _fitApplied = false;
                return;
            }

            var wrote = false;
            foreach (var mesh in _skinMeshes)
            {
                if (RewriteVertices(mesh, position, euler, scale)) wrote = true;
            }
            if (!wrote)
            {
                AssignSources();
                DestroyClones();
                _fitApplied = false;
                return;
            }

            AssignClones();
        }

        private static bool _unreadableWarned;

        // Writes a mesh copy. The clone keeps the source bind poses so skinning still matches
        // the body bones the cape was remapped onto.
        private static bool RewriteVertices(SkinMesh mesh, Vector3 position, Vector3 euler, Vector3 scale)
        {
            if (mesh.Source == null || !mesh.Source.isReadable)
            {
                if (!_unreadableWarned && mesh.Source != null)
                {
                    _unreadableWarned = true;
                    Logger.LogWarning(
                        "Armor mesh is not CPU-readable, so its scale and offset were left alone: " +
                        mesh.Source.name);
                }

                return false;
            }

            if (mesh.Clone == null)
            {
                mesh.Clone = Instantiate(mesh.Source);
                mesh.Clone.name = mesh.Source.name + " (fit)";
            }

            var rotation = Quaternion.Euler(euler);
            var vertices = mesh.Source.vertices;
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] = FitVertex(mesh.Center, position, rotation, scale, vertices[i]);
            mesh.Clone.vertices = vertices;
            var normals = mesh.Source.normals;
            if (normals != null && normals.Length == vertices.Length)
            {
                for (var i = 0; i < normals.Length; i++)
                    normals[i] = FitDirection(rotation, scale, normals[i]);
                mesh.Clone.normals = normals;
            }

            var tangents = mesh.Source.tangents;
            if (tangents != null && tangents.Length == vertices.Length)
            {
                for (var i = 0; i < tangents.Length; i++)
                {
                    var tangent = tangents[i];
                    var direction = FitDirection(rotation, scale, new Vector3(tangent.x, tangent.y, tangent.z));
                    tangents[i] = new Vector4(direction.x, direction.y, direction.z, tangent.w);
                }

                mesh.Clone.tangents = tangents;
            }

            mesh.Clone.bindposes = mesh.Source.bindposes;
            mesh.Clone.RecalculateBounds();
            return true;
        }

        private static Vector3 FitVertex(Vector3 center, Vector3 position, Quaternion rotation, Vector3 scale,
            Vector3 vertex)
        {
            return AttachmentTransforms.Vector(AttachmentMath.FitSkinnedVertex(
                AttachmentTransforms.Vector(center),
                AttachmentTransforms.Vector(position),
                AttachmentTransforms.Rotation(rotation),
                AttachmentTransforms.Vector(scale),
                AttachmentTransforms.Vector(vertex)));
        }

        private static Vector3 FitDirection(Quaternion rotation, Vector3 scale, Vector3 direction)
        {
            return AttachmentTransforms.Vector(AttachmentMath.FitSkinnedDirection(
                AttachmentTransforms.Rotation(rotation),
                AttachmentTransforms.Vector(scale),
                AttachmentTransforms.Vector(direction)));
        }

        public void ClearSkinnedFit()
        {
            if (!_fitApplied && !ClonesAssigned()) return;
            AssignSources();
            DestroyClones();
            _fitApplied = false;
        }

        private void CacheSkins()
        {
            if (_skinsCached) return;
            _skinsCached = true;
            _skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            _skinSources = new Mesh[_skins.Length];
            for (var i = 0; i < _skins.Length; i++)
            {
                var mesh = _skins[i].sharedMesh;
                _skinSources[i] = mesh;
                if (mesh == null || mesh.bindposes.Length == 0) continue;
                if (_skinMeshes.Exists(existing => existing.Source == mesh)) continue;
                _skinMeshes.Add(new SkinMesh { Source = mesh, Center = mesh.bounds.center });
            }
        }

        private bool ClonesAssigned()
        {
            if (_skins == null || _skinSources == null) return false;
            for (var i = 0; i < _skins.Length; i++)
            {
                var skin = _skins[i];
                var source = _skinSources[i];
                if (skin == null || source == null || source.bindposes.Length == 0) continue;
                var fitted = _skinMeshes.Find(mesh => mesh.Source == source);
                if (fitted == null || fitted.Clone == null || skin.sharedMesh != fitted.Clone) return false;
            }

            return _skinMeshes.Count > 0;
        }

        private void AssignClones()
        {
            for (var i = 0; i < _skins.Length; i++)
            {
                var skin = _skins[i];
                var source = _skinSources[i];
                if (skin == null || source == null) continue;
                var fitted = _skinMeshes.Find(mesh => mesh.Source == source);
                if (fitted?.Clone == null) continue;
                SwapMesh(skin, fitted.Clone, true);
            }
        }

        private void AssignSources()
        {
            if (_skins == null || _skinSources == null) return;
            for (var i = 0; i < _skins.Length; i++)
            {
                if (_skins[i] == null || _skinSources[i] == null) continue;
                SwapMesh(_skins[i], _skinSources[i], false);
            }
        }

        // Cloth keeps the rest shape it was enabled with. Suspend it before the swap, then
        // turn it back on so it rebuilds from these vertices. Bind poses stay as authored.
        private static void SwapMesh(SkinnedMeshRenderer skin, Mesh mesh, bool updateWhenOffscreen)
        {
            var cloth = skin.GetComponent<Cloth>();
            var clothEnabled = cloth != null && cloth.enabled;
            if (cloth != null) cloth.enabled = false;
            skin.sharedMesh = mesh;
            if (updateWhenOffscreen) skin.updateWhenOffscreen = true;
            if (cloth != null) cloth.enabled = clothEnabled;
        }

        private void DestroyClones()
        {
            foreach (var mesh in _skinMeshes)
            {
                if (mesh.Clone == null) continue;
                Destroy(mesh.Clone);
                mesh.Clone = null;
            }
        }

        private void OnDestroy()
        {
            DestroyClones();
        }
    }
}
