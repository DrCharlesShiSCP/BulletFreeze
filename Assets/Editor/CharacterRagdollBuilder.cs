using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class CharacterRagdollBuilder
{
    private static readonly string[] CharacterPrefabPaths =
    {
        "Assets/Prefabs/Big Vegas.prefab",
        "Assets/Prefabs/claire.prefab",
        "Assets/Prefabs/Michelle.prefab",
        "Assets/Prefabs/The Boss.prefab"
    };

    [MenuItem("Tools/Bullet Freeze/Rebuild Character Ragdolls")]
    public static void RebuildCharacterRagdolls()
    {
        int successCount = 0;

        foreach (string prefabPath in CharacterPrefabPaths)
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

            try
            {
                if (!TryBuildRagdoll(prefabRoot, out string message))
                {
                    Debug.LogWarning(
                        $"[CharacterRagdollBuilder] Skipped '{prefabPath}'. {message}");
                    continue;
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                successCount++;
                Debug.Log($"[CharacterRagdollBuilder] Rebuilt ragdoll for '{prefabPath}'.");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[CharacterRagdollBuilder] Failed to build ragdoll for '{prefabPath}'. {exception}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[CharacterRagdollBuilder] Finished. Successfully rebuilt {successCount}/" +
            $"{CharacterPrefabPaths.Length} character ragdolls.");
    }

    private static bool TryBuildRagdoll(GameObject prefabRoot, out string message)
    {
        message = string.Empty;

        if (prefabRoot == null)
        {
            message = "Prefab root could not be loaded.";
            return false;
        }

        Animator animator = prefabRoot.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            message = "No Animator was found.";
            return false;
        }

        if (animator.avatar == null || !animator.avatar.isHuman)
        {
            message = "Animator avatar is not configured as Humanoid.";
            return false;
        }

        Dictionary<HumanBodyBones, Transform> bones = CollectBones(animator);
        if (!bones.ContainsKey(HumanBodyBones.Hips))
        {
            message = "Humanoid hips bone is missing.";
            return false;
        }

        Dictionary<HumanBodyBones, Rigidbody> rigidbodies = new Dictionary<HumanBodyBones, Rigidbody>();

        foreach (BoneDefinition definition in CreateDefinitions())
        {
            if (!bones.TryGetValue(definition.Bone, out Transform bone) || bone == null)
                continue;

            Collider existingCollider = bone.GetComponent<Collider>();
            Rigidbody rigidbody = GetOrAddComponent<Rigidbody>(bone.gameObject);
            rigidbody.mass = definition.Mass;
            rigidbody.linearDamping = 0f;
            rigidbody.angularDamping = 0.05f;
            rigidbody.useGravity = true;
            rigidbody.isKinematic = true;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbodies[definition.Bone] = rigidbody;

            if (existingCollider == null)
                AddColliderForBone(definition, bone, bones);
            else
                existingCollider.enabled = false;
        }

        foreach (BoneDefinition definition in CreateDefinitions())
        {
            if (definition.ParentBone == null)
                continue;

            if (!bones.TryGetValue(definition.Bone, out Transform bone) || bone == null)
                continue;

            if (!rigidbodies.TryGetValue(definition.Bone, out Rigidbody rigidbody) || rigidbody == null)
                continue;

            if (!rigidbodies.TryGetValue(definition.ParentBone.Value, out Rigidbody parentBody) || parentBody == null)
                continue;

            CharacterJoint joint = GetOrAddComponent<CharacterJoint>(bone.gameObject);
            joint.connectedBody = parentBody;
            joint.enableCollision = false;
            joint.enableProjection = true;
            joint.axis = Vector3.right;
            joint.swingAxis = Vector3.up;

            SoftJointLimit lowTwist = joint.lowTwistLimit;
            lowTwist.limit = definition.LowTwistLimit;
            joint.lowTwistLimit = lowTwist;

            SoftJointLimit highTwist = joint.highTwistLimit;
            highTwist.limit = definition.HighTwistLimit;
            joint.highTwistLimit = highTwist;

            SoftJointLimit swing1 = joint.swing1Limit;
            swing1.limit = definition.Swing1Limit;
            joint.swing1Limit = swing1;

            SoftJointLimit swing2 = joint.swing2Limit;
            swing2.limit = definition.Swing2Limit;
            joint.swing2Limit = swing2;
        }

        return true;
    }

    private static Dictionary<HumanBodyBones, Transform> CollectBones(Animator animator)
    {
        Dictionary<HumanBodyBones, Transform> bones = new Dictionary<HumanBodyBones, Transform>();

        foreach (BoneDefinition definition in CreateDefinitions())
        {
            Transform bone = animator.GetBoneTransform(definition.Bone);
            if (bone != null && !bones.ContainsKey(definition.Bone))
                bones.Add(definition.Bone, bone);

            if (definition.NextBone != null)
            {
                Transform nextBone = animator.GetBoneTransform(definition.NextBone.Value);
                if (nextBone != null && !bones.ContainsKey(definition.NextBone.Value))
                    bones.Add(definition.NextBone.Value, nextBone);
            }

            if (definition.ParentBone != null)
            {
                Transform parentBone = animator.GetBoneTransform(definition.ParentBone.Value);
                if (parentBone != null && !bones.ContainsKey(definition.ParentBone.Value))
                    bones.Add(definition.ParentBone.Value, parentBone);
            }
        }

        return bones;
    }

    private static void AddColliderForBone(
        BoneDefinition definition,
        Transform bone,
        IReadOnlyDictionary<HumanBodyBones, Transform> bones)
    {
        switch (definition.ColliderKind)
        {
            case BoneColliderKind.Box:
                ConfigureBoxCollider(GetOrAddComponent<BoxCollider>(bone.gameObject), definition, bone, bones);
                break;

            case BoneColliderKind.Sphere:
                ConfigureSphereCollider(GetOrAddComponent<SphereCollider>(bone.gameObject), definition, bone, bones);
                break;

            default:
                ConfigureCapsuleCollider(GetOrAddComponent<CapsuleCollider>(bone.gameObject), definition, bone, bones);
                break;
        }
    }

    private static void ConfigureCapsuleCollider(
        CapsuleCollider collider,
        BoneDefinition definition,
        Transform bone,
        IReadOnlyDictionary<HumanBodyBones, Transform> bones)
    {
        Vector3 localOffset = GetLocalOffsetToNextBone(definition, bone, bones);
        float length = Mathf.Max(0.08f, localOffset.magnitude);
        int axis = GetMajorAxis(localOffset);

        collider.direction = axis;
        collider.center = localOffset * 0.5f;
        collider.height = Mathf.Max(length, definition.MinimumRadius * 2f);
        collider.radius = Mathf.Max(definition.MinimumRadius, length * definition.RadiusFactor);
        collider.enabled = false;
    }

    private static void ConfigureBoxCollider(
        BoxCollider collider,
        BoneDefinition definition,
        Transform bone,
        IReadOnlyDictionary<HumanBodyBones, Transform> bones)
    {
        Vector3 localOffset = GetLocalOffsetToNextBone(definition, bone, bones);
        float length = Mathf.Max(0.12f, localOffset.magnitude);
        int axis = GetMajorAxis(localOffset);

        collider.center = localOffset * 0.5f;

        Vector3 size = definition.BoxSize;
        if (axis == 0)
            size.x = Mathf.Max(size.x, length);
        else if (axis == 1)
            size.y = Mathf.Max(size.y, length);
        else
            size.z = Mathf.Max(size.z, length);

        collider.size = size;
        collider.enabled = false;
    }

    private static void ConfigureSphereCollider(
        SphereCollider collider,
        BoneDefinition definition,
        Transform bone,
        IReadOnlyDictionary<HumanBodyBones, Transform> bones)
    {
        Vector3 localOffset = GetLocalOffsetToNextBone(definition, bone, bones);
        float radius = localOffset.sqrMagnitude > 0.0001f
            ? Mathf.Max(definition.MinimumRadius, localOffset.magnitude * definition.RadiusFactor)
            : definition.MinimumRadius;

        collider.center = localOffset.sqrMagnitude > 0.0001f ? localOffset * 0.35f : definition.CenterOffset;
        collider.radius = radius;
        collider.enabled = false;
    }

    private static Vector3 GetLocalOffsetToNextBone(
        BoneDefinition definition,
        Transform bone,
        IReadOnlyDictionary<HumanBodyBones, Transform> bones)
    {
        Transform nextBone = null;

        if (definition.NextBone != null)
            bones.TryGetValue(definition.NextBone.Value, out nextBone);

        if (nextBone == null)
            nextBone = GetFirstTransformChild(bone);

        if (nextBone == null)
            return definition.FallbackDirection * definition.FallbackLength;

        return bone.InverseTransformPoint(nextBone.position);
    }

    private static Transform GetFirstTransformChild(Transform transform)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child != null)
                return child;
        }

        return null;
    }

    private static int GetMajorAxis(Vector3 direction)
    {
        Vector3 absolute = new Vector3(
            Mathf.Abs(direction.x),
            Mathf.Abs(direction.y),
            Mathf.Abs(direction.z));

        if (absolute.x >= absolute.y && absolute.x >= absolute.z)
            return 0;

        if (absolute.y >= absolute.x && absolute.y >= absolute.z)
            return 1;

        return 2;
    }

    private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }

    private static IEnumerable<BoneDefinition> CreateDefinitions()
    {
        HumanBodyBones chestBone = HumanBodyBones.Chest;

        yield return new BoneDefinition(HumanBodyBones.Hips, null, HumanBodyBones.Spine, BoneColliderKind.Box)
        {
            Mass = 2.4f,
            BoxSize = new Vector3(0.28f, 0.22f, 0.22f),
            FallbackDirection = Vector3.up,
            FallbackLength = 0.25f
        };
        yield return new BoneDefinition(HumanBodyBones.Spine, HumanBodyBones.Hips, chestBone, BoneColliderKind.Box)
        {
            Mass = 1.5f,
            BoxSize = new Vector3(0.24f, 0.24f, 0.2f),
            FallbackDirection = Vector3.up,
            FallbackLength = 0.22f,
            LowTwistLimit = -15f,
            HighTwistLimit = 15f,
            Swing1Limit = 20f,
            Swing2Limit = 15f
        };
        yield return new BoneDefinition(chestBone, HumanBodyBones.Spine, HumanBodyBones.Head, BoneColliderKind.Box)
        {
            Mass = 1.4f,
            BoxSize = new Vector3(0.28f, 0.28f, 0.22f),
            FallbackDirection = Vector3.up,
            FallbackLength = 0.24f,
            LowTwistLimit = -20f,
            HighTwistLimit = 20f,
            Swing1Limit = 25f,
            Swing2Limit = 20f
        };
        yield return new BoneDefinition(HumanBodyBones.Head, chestBone, null, BoneColliderKind.Sphere)
        {
            Mass = 0.8f,
            MinimumRadius = 0.1f,
            RadiusFactor = 0.25f,
            CenterOffset = new Vector3(0f, 0.12f, 0f),
            FallbackDirection = Vector3.up,
            FallbackLength = 0.18f,
            LowTwistLimit = -25f,
            HighTwistLimit = 25f,
            Swing1Limit = 30f,
            Swing2Limit = 25f
        };

        foreach (BoneDefinition armDefinition in CreateArmDefinitions(true, chestBone))
            yield return armDefinition;
        foreach (BoneDefinition armDefinition in CreateArmDefinitions(false, chestBone))
            yield return armDefinition;
        foreach (BoneDefinition legDefinition in CreateLegDefinitions(true))
            yield return legDefinition;
        foreach (BoneDefinition legDefinition in CreateLegDefinitions(false))
            yield return legDefinition;
    }

    private static IEnumerable<BoneDefinition> CreateArmDefinitions(bool left, HumanBodyBones chestBone)
    {
        HumanBodyBones upperArm = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
        HumanBodyBones lowerArm = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
        HumanBodyBones hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;

        yield return new BoneDefinition(upperArm, chestBone, lowerArm, BoneColliderKind.Capsule)
        {
            Mass = 0.65f,
            MinimumRadius = 0.05f,
            RadiusFactor = 0.22f,
            FallbackDirection = left ? Vector3.left : Vector3.right,
            FallbackLength = 0.22f,
            LowTwistLimit = -45f,
            HighTwistLimit = 45f,
            Swing1Limit = 60f,
            Swing2Limit = 35f
        };
        yield return new BoneDefinition(lowerArm, upperArm, hand, BoneColliderKind.Capsule)
        {
            Mass = 0.45f,
            MinimumRadius = 0.045f,
            RadiusFactor = 0.2f,
            FallbackDirection = left ? Vector3.left : Vector3.right,
            FallbackLength = 0.22f,
            LowTwistLimit = -55f,
            HighTwistLimit = 10f,
            Swing1Limit = 25f,
            Swing2Limit = 20f
        };
        yield return new BoneDefinition(hand, lowerArm, null, BoneColliderKind.Box)
        {
            Mass = 0.2f,
            BoxSize = new Vector3(0.1f, 0.05f, 0.14f),
            FallbackDirection = left ? Vector3.left : Vector3.right,
            FallbackLength = 0.12f,
            LowTwistLimit = -20f,
            HighTwistLimit = 20f,
            Swing1Limit = 20f,
            Swing2Limit = 10f
        };
    }

    private static IEnumerable<BoneDefinition> CreateLegDefinitions(bool left)
    {
        HumanBodyBones upperLeg = left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
        HumanBodyBones lowerLeg = left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
        HumanBodyBones foot = left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;

        yield return new BoneDefinition(upperLeg, HumanBodyBones.Hips, lowerLeg, BoneColliderKind.Capsule)
        {
            Mass = 1.2f,
            MinimumRadius = 0.06f,
            RadiusFactor = 0.22f,
            FallbackDirection = Vector3.down,
            FallbackLength = 0.35f,
            LowTwistLimit = -15f,
            HighTwistLimit = 35f,
            Swing1Limit = 45f,
            Swing2Limit = 20f
        };
        yield return new BoneDefinition(lowerLeg, upperLeg, foot, BoneColliderKind.Capsule)
        {
            Mass = 0.9f,
            MinimumRadius = 0.055f,
            RadiusFactor = 0.18f,
            FallbackDirection = Vector3.down,
            FallbackLength = 0.35f,
            LowTwistLimit = -5f,
            HighTwistLimit = 55f,
            Swing1Limit = 10f,
            Swing2Limit = 10f
        };
        yield return new BoneDefinition(foot, lowerLeg, null, BoneColliderKind.Box)
        {
            Mass = 0.35f,
            BoxSize = new Vector3(0.12f, 0.06f, 0.22f),
            FallbackDirection = Vector3.forward,
            FallbackLength = 0.18f,
            LowTwistLimit = -15f,
            HighTwistLimit = 15f,
            Swing1Limit = 10f,
            Swing2Limit = 10f
        };
    }

    private enum BoneColliderKind
    {
        Capsule,
        Box,
        Sphere
    }

    private sealed class BoneDefinition
    {
        public BoneDefinition(
            HumanBodyBones bone,
            HumanBodyBones? parentBone,
            HumanBodyBones? nextBone,
            BoneColliderKind colliderKind)
        {
            Bone = bone;
            ParentBone = parentBone;
            NextBone = nextBone;
            ColliderKind = colliderKind;
        }

        public HumanBodyBones Bone { get; }
        public HumanBodyBones? ParentBone { get; }
        public HumanBodyBones? NextBone { get; }
        public BoneColliderKind ColliderKind { get; }
        public float Mass { get; set; } = 1f;
        public float MinimumRadius { get; set; } = 0.06f;
        public float RadiusFactor { get; set; } = 0.2f;
        public Vector3 BoxSize { get; set; } = new Vector3(0.18f, 0.18f, 0.18f);
        public Vector3 CenterOffset { get; set; } = Vector3.zero;
        public Vector3 FallbackDirection { get; set; } = Vector3.up;
        public float FallbackLength { get; set; } = 0.15f;
        public float LowTwistLimit { get; set; } = -20f;
        public float HighTwistLimit { get; set; } = 20f;
        public float Swing1Limit { get; set; } = 20f;
        public float Swing2Limit { get; set; } = 20f;
    }
}
