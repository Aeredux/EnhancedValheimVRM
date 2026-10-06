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
            public Matrix4x4[] Bindposes;
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

        // Capes and most chest and leg pieces are skinned onto the body skeleton. Moving this
        // object does nothing to those vertices, so the same Pos/Rot/Scale is baked into a
        // clone of the mesh bind poses. <1, 1, 1> and a zero offset put the original mesh back.
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

            var rotation = AttachmentTransforms.Rotation(Quaternion.Euler(euler));
            var moved = AttachmentTransforms.Vector(position);
            var sized = AttachmentTransforms.Vector(scale);
            foreach (var mesh in _skinMeshes)
            {
                if (mesh.Clone == null)
                {
                    mesh.Clone = Instantiate(mesh.Source);
                    mesh.Clone.name = mesh.Source.name + " (fit)";
                }

                var around = AttachmentTransforms.Matrix(AttachmentMath.SkinnedPieceFit(
                    AttachmentTransforms.Vector(mesh.Center), moved, rotation, sized));
                var poses = new Matrix4x4[mesh.Bindposes.Length];
                for (var i = 0; i < poses.Length; i++) poses[i] = mesh.Bindposes[i] * around;
                mesh.Clone.bindposes = poses;
            }

            AssignClones();
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
                _skinMeshes.Add(new SkinMesh
                {
                    Source = mesh,
                    Bindposes = mesh.bindposes,
                    Center = mesh.bounds.center
                });
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
                skin.sharedMesh = fitted.Clone;
                skin.updateWhenOffscreen = true;
                RefreshCloth(skin);
            }
        }

        private void AssignSources()
        {
            if (_skins == null || _skinSources == null) return;
            for (var i = 0; i < _skins.Length; i++)
            {
                if (_skins[i] == null || _skinSources[i] == null) continue;
                _skins[i].sharedMesh = _skinSources[i];
                RefreshCloth(_skins[i]);
            }
        }

        // Cloth keeps the mesh it was enabled with. A cape is cloth on a skinned renderer,
        // so turn it off and on once the bind-pose clone is assigned.
        private static void RefreshCloth(SkinnedMeshRenderer skin)
        {
            var cloth = skin.GetComponent<Cloth>();
            if (cloth == null) return;
            var enabled = cloth.enabled;
            cloth.enabled = false;
            cloth.enabled = enabled;
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
