using Colossal.Entities;
using Colossal.Mathematics;
using Game.City;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ObjectTransform = Game.Objects.Transform;

namespace BetaTestDrivingMod
{
    public sealed partial class DirectDriveControlSystem
    {
        private const float kVehicleCollisionScanRadius = 14.5f;
        private const float kVehicleCollisionSkin = 0.04f;
        private const float kVehicleCollisionMinHalfWidth = 0.85f;
        private const float kVehicleCollisionMinHalfLength = 1.65f;
        private const float kVehicleCollisionWidthScale = 0.98f;
        private const float kVehicleCollisionLengthScale = 0.98f;
        private const float kVehicleCollisionEndWidthScale = 0.68f;
        private const float kVehicleCollisionMiddleLengthScale = 0.58f;
        private const float kVehicleCollisionEndLengthScale = 0.23f;
        private const float kVehicleCollisionEndOffsetScale = 0.73f;
        private const float kVehicleBumperSweepProbeRadius = 0.32f;
        private const float kVehicleBumperSweepSideInset = 0.22f;
        private const float kVehicleBumperSweepContactBackoff = 0.03f;
        private const float kVehicleBumperSweepResolvePadding = 0.05f;
        private const float kVehicleBumperSweepMinTravelSq = 0.0004f;
        private const float kVehicleCollisionVerticalPadding = 0.75f;
        private const float kVehicleMeshSweepFallbackMinHeight = 0.32f;
        private const float kVehicleMeshSweepFallbackMaxHeight = 1.55f;
        private const float kVehicleCollisionBroadphasePadding = 0.75f;
        private const float kVehicleCollisionFramePoseScanPadding = 32f;
        private const float kVehicleCollisionFramePoseMaxDistanceSq = 80f * 80f;
        private const uint kVehicleCollisionCandidateRefreshFrames = 8U;
        private const float kVehicleCollisionCandidateCachePadding = 18f;
        private const float kVehicleCollisionCandidateRefreshDistanceSq = 64f;
        private CityConfigurationSystem m_CityConfigurationSystem;
        private EntityQuery m_CollisionCarQuery;
        private Entity m_LastVehicleCollisionTarget = Entity.Null;
        private uint m_LastVehicleCollisionFrame;
        private uint m_LastVehicleCollisionLogFrame;
        private readonly List<Entity> m_VehicleCollisionCandidateCache = new List<Entity>();
        private readonly Dictionary<Entity, Vector3> m_VehicleCollisionSizeCache = new Dictionary<Entity, Vector3>();
        private bool m_VehicleCollisionCandidateCacheValid;
        private uint m_VehicleCollisionCandidateCacheFrame;
        private Vector3 m_VehicleCollisionCandidateCacheCenter;
        private bool m_FixedCollisionBoundsLogged;

        private bool TryResolveVehicleCollision(Entity car, Vector3 previousPosition, Quaternion rotation, Vector3 forward, ref Vector3 position, ref Vector3 velocity, ref float speedMps, out Entity hit)
        {
            hit = Entity.Null;
            bool debugEnabled = DirectDriveRuntime.VisualCollisionDebugEnabled;
            if (!DirectDriveRuntime.VehicleCollisionEnabled)
            {
                if (debugEnabled)
                    DirectDriveRuntime.ClearCollisionDebug();

                return false;
            }

            EnsureVehicleCollisionQuery();
            if (m_CollisionCarQuery == default || m_CollisionCarQuery.IsEmptyIgnoreFilter)
            {
                if (debugEnabled)
                    DirectDriveRuntime.ClearCollisionDebug();

                return false;
            }

            Vector3 travel = position - previousPosition;
            if (travel.sqrMagnitude < kVehicleBumperSweepMinTravelSq)
            {
                if (debugEnabled && TryGetVehicleCollisionShape(car, position, rotation, out VehicleCollisionShape parkedShape))
                    PublishCollisionDebug(parkedShape, default, false, Entity.Null, false, "no sweep: car barely moved", parkedShape.Body.Center, parkedShape.Body.Center);
                else if (debugEnabled)
                    DirectDriveRuntime.ClearCollisionDebug();

                return false;
            }

            if (!TryGetVehicleCollisionShape(car, previousPosition, rotation, out VehicleCollisionShape previousSelfShape) ||
                !TryGetVehicleCollisionShape(car, position, rotation, out VehicleCollisionShape selfShape))
            {
                if (debugEnabled)
                    DirectDriveRuntime.ClearCollisionDebug();

                return false;
            }

            bool foundHit = false;
            float bestHitT = 2f;
            Vector3 bestNormal = Vector3.zero;
            Entity bestHit = Entity.Null;
            VehicleCollisionShape bestHitShape = default;
            bool debugHasTarget = false;
            Entity debugTarget = Entity.Null;
            VehicleCollisionShape debugTargetShape = default;
            float debugTargetDistanceSq = float.MaxValue;
            string debugStatus = "no nearby collision target";
            float scanRadiusSq = kVehicleCollisionScanRadius * kVehicleCollisionScanRadius;
            float framePoseScanRadius = kVehicleCollisionScanRadius + kVehicleCollisionFramePoseScanPadding;
            float framePoseScanRadiusSq = framePoseScanRadius * framePoseScanRadius;
            uint frame = m_SimulationSystem.frameIndex;
            float candidateCacheRadius = framePoseScanRadius + kVehicleCollisionCandidateCachePadding;
            RefreshVehicleCollisionCandidateCache(car, position, candidateCacheRadius * candidateCacheRadius);
            for (int i = 0; i < m_VehicleCollisionCandidateCache.Count; i++)
            {
                Entity candidate = m_VehicleCollisionCandidateCache[i];
                if (candidate == car ||
                    candidate == Entity.Null ||
                    !EntityManager.Exists(candidate) ||
                    !EntityManager.TryGetComponent(candidate, out ObjectTransform otherTransform))
                {
                    continue;
                }

                Vector3 rawOtherPosition = ToUnityVector(otherTransform.m_Position);
                if (!IsFinite(rawOtherPosition) ||
                    (rawOtherPosition - position).sqrMagnitude > framePoseScanRadiusSq)
                {
                    continue;
                }

                if (!TryGetVehicleCollisionPose(candidate, otherTransform, out Vector3 otherPosition, out Quaternion otherRotation))
                    continue;

                if ((otherPosition - position).sqrMagnitude > scanRadiusSq)
                    continue;

                if (!TryGetVehicleCollisionShape(candidate, otherPosition, otherRotation, out VehicleCollisionShape otherShape))
                    continue;

                if (!SweptShapeOverlapsVertically(previousSelfShape, selfShape, otherShape, kVehicleCollisionVerticalPadding))
                {
                    if (debugEnabled && debugStatus == "no nearby collision target")
                        debugStatus = $"skipped {candidate.Index}: different road height";

                    continue;
                }

                if (debugEnabled)
                {
                    float targetDistanceSq = (otherShape.Body.Center - selfShape.Body.Center).sqrMagnitude;
                    if (targetDistanceSq < debugTargetDistanceSq)
                    {
                        debugHasTarget = true;
                        debugTarget = candidate;
                        debugTargetShape = otherShape;
                        debugTargetDistanceSq = targetDistanceSq;
                        debugStatus = $"target {candidate.Index}: outside swept broadphase";
                    }
                }

                if (!IsSweptShapeNearShape(previousSelfShape, selfShape, otherShape, kVehicleCollisionBroadphasePadding))
                    continue;

                if (debugEnabled && candidate == debugTarget)
                    debugStatus = $"target {candidate.Index}: broadphase ok";

                bool hitCandidate = TrySweepVehicleBumper(previousSelfShape, selfShape, otherShape, forward, speedMps, out float hitT, out Vector3 normal);
                if (debugEnabled && candidate == debugTarget)
                    debugStatus = hitCandidate ? $"target {candidate.Index}: shape-sweep hit" : $"target {candidate.Index}: shape-sweep miss";

                if (!hitCandidate)
                    continue;

                if (hitT < bestHitT)
                {
                    foundHit = true;
                    bestHitT = hitT;
                    bestNormal = normal;
                    bestHit = candidate;
                    bestHitShape = otherShape;
                }
            }

            if (!foundHit)
            {
                if (debugEnabled)
                    PublishCollisionDebug(selfShape, debugTargetShape, debugHasTarget, debugTarget, false, debugStatus, previousSelfShape.Body.Center, selfShape.Body.Center);

                return false;
            }

            float contactT = Mathf.Clamp01(bestHitT - kVehicleBumperSweepContactBackoff);
            position = previousPosition + travel * contactT + bestNormal * kVehicleBumperSweepResolvePadding;
            hit = bestHit;
            m_LastVehicleCollisionTarget = bestHit;
            m_LastVehicleCollisionFrame = frame;
            if (debugEnabled)
                PublishCollisionDebug(selfShape, bestHitShape, true, bestHit, true, $"hit {bestHit.Index} t={bestHitT:0.00}", previousSelfShape.Body.Center, selfShape.Body.Center);

            if (frame == 0U || m_LastVehicleCollisionLogFrame == 0U || frame - m_LastVehicleCollisionLogFrame > 45U)
            {
                Mod.log.Info($"Direct Drive vehicle collision resolved against {bestHit}; using fixed crashguard hitboxes.");
                m_LastVehicleCollisionLogFrame = frame;
            }

            float preImpactSpeed = speedMps;
            float intoObstacle = Vector3.Dot(velocity, bestNormal);
            if (intoObstacle < 0f)
                velocity -= bestNormal * (intoObstacle * 0.75f);

            float projectedSpeed = Vector3.Dot(velocity, forward);
            if (Mathf.Abs(projectedSpeed) < Mathf.Abs(speedMps) || Mathf.Sign(projectedSpeed) == Mathf.Sign(speedMps))
                speedMps = Mathf.Abs(projectedSpeed) < 0.2f ? 0f : projectedSpeed;

            float retainedSpeed = Mathf.Sign(preImpactSpeed) * Mathf.Abs(preImpactSpeed) * ClampFinite(DirectDriveRuntime.CollisionRetainedSpeed, 0.35f, 0f, 1f);
            if (Mathf.Abs(speedMps) < Mathf.Abs(retainedSpeed))
                speedMps = retainedSpeed;

            velocity = forward * speedMps;
            return true;
        }

        private void PublishCollisionDebug(VehicleCollisionShape selfShape, VehicleCollisionShape targetShape, bool hasTarget, Entity target, bool hit, string status, Vector3 sweepStart, Vector3 sweepEnd)
        {
            DirectDriveRuntime.SetCollisionDebug(
                selfShape.Body.Center,
                selfShape.Body.Right,
                selfShape.Body.Forward,
                selfShape.Body.HalfWidth,
                selfShape.Body.HalfLength,
                selfShape.MinHeight,
                selfShape.MaxHeight,
                sweepStart,
                sweepEnd,
                hasTarget,
                hasTarget ? targetShape.Body.Center : Vector3.zero,
                hasTarget ? targetShape.Body.Right : Vector3.right,
                hasTarget ? targetShape.Body.Forward : Vector3.forward,
                hasTarget ? targetShape.Body.HalfWidth : 0f,
                hasTarget ? targetShape.Body.HalfLength : 0f,
                hasTarget ? targetShape.MinHeight : 0f,
                hasTarget ? targetShape.MaxHeight : 1.5f,
                hit,
                hasTarget && target != Entity.Null ? $"{status} entity={target.Index}" : status);
        }

        private void RefreshVehicleCollisionCandidateCache(Entity car, Vector3 position, float cacheRadiusSq)
        {
            uint frame = m_SimulationSystem != null ? m_SimulationSystem.frameIndex : 0U;
            bool expired = !m_VehicleCollisionCandidateCacheValid ||
                frame == 0U ||
                frame - m_VehicleCollisionCandidateCacheFrame >= kVehicleCollisionCandidateRefreshFrames ||
                (position - m_VehicleCollisionCandidateCacheCenter).sqrMagnitude > kVehicleCollisionCandidateRefreshDistanceSq;
            if (!expired)
                return;

            m_VehicleCollisionCandidateCache.Clear();
            m_VehicleCollisionCandidateCacheCenter = position;
            m_VehicleCollisionCandidateCacheFrame = frame;
            m_VehicleCollisionCandidateCacheValid = true;

            EnsureVehicleCollisionQuery();
            if (m_CollisionCarQuery == default || m_CollisionCarQuery.IsEmptyIgnoreFilter)
                return;

            NativeArray<Entity> entities = m_CollisionCarQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity candidate = entities[i];
                    if (candidate == car ||
                        candidate == Entity.Null ||
                        !EntityManager.Exists(candidate) ||
                        !EntityManager.TryGetComponent(candidate, out ObjectTransform transform))
                    {
                        continue;
                    }

                    Vector3 candidatePosition = ToUnityVector(transform.m_Position);
                    if (!IsFinite(candidatePosition))
                        continue;

                    Vector3 delta = candidatePosition - position;
                    delta.y = 0f;
                    if (delta.sqrMagnitude <= cacheRadiusSq)
                        m_VehicleCollisionCandidateCache.Add(candidate);
                }
            }
            finally
            {
                entities.Dispose();
            }
        }

        private void InvalidateVehicleCollisionCandidateCache()
        {
            m_VehicleCollisionCandidateCache.Clear();
            m_VehicleCollisionCandidateCacheValid = false;
            m_VehicleCollisionCandidateCacheFrame = 0U;
            m_VehicleCollisionCandidateCacheCenter = Vector3.zero;
        }

        private void EnsureVehicleCollisionQuery()
        {
            if (m_CollisionCarQuery != default)
                return;

            m_CollisionCarQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Car>(),
                    ComponentType.ReadOnly<CarNavigation>(),
                    ComponentType.ReadOnly<CarCurrentLane>(),
                    ComponentType.ReadOnly<ObjectTransform>(),
                    ComponentType.ReadOnly<Moving>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<TransformFrame>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<TripSource>(),
                    ComponentType.ReadOnly<ParkedCar>(),
                    ComponentType.ReadOnly<Unspawned>(),
                    ComponentType.ReadOnly<Destroyed>(),
                    ComponentType.ReadOnly<Bicycle>()
                }
            });
        }

        private bool TryGetVehicleCollisionPose(Entity vehicle, ObjectTransform fallbackTransform, out Vector3 position, out Quaternion rotation)
        {
            position = ToUnityVector(fallbackTransform.m_Position);
            Quaternion fallbackRotation = ToUnityQuaternion(fallbackTransform.m_Rotation);
            rotation = Quaternion.identity;
            if (!IsFinite(position) || !IsFinite(fallbackRotation))
                return false;

            rotation = LevelRotation(fallbackRotation);
            if (!EntityManager.HasBuffer<TransformFrame>(vehicle))
                return false;

            DynamicBuffer<TransformFrame> frames = EntityManager.GetBuffer<TransformFrame>(vehicle, true);
            if (frames.Length == 0)
                return false;

            try
            {
                UpdateFrame updateFrame = EntityManager.GetSharedComponentManaged<UpdateFrame>(vehicle);
                uint simulationFrame = m_SimulationSystem != null ? m_SimulationSystem.frameIndex : 0U;
                ObjectInterpolateSystem.CalculateUpdateFrames(
                    simulationFrame,
                    0f,
                    updateFrame.m_Index,
                    out uint updateFrameA,
                    out uint updateFrameB,
                    out float framePosition);

                TransformFrame frameA = frames[Mathf.Clamp((int)updateFrameA, 0, frames.Length - 1)];
                TransformFrame frameB = frames[Mathf.Clamp((int)updateFrameB, 0, frames.Length - 1)];
                Vector3 framePositionA = ToUnityVector(frameA.m_Position);
                Vector3 framePositionB = ToUnityVector(frameB.m_Position);
                Quaternion frameRotationA = ToUnityQuaternion(frameA.m_Rotation);
                Quaternion frameRotationB = ToUnityQuaternion(frameB.m_Rotation);
                if (!IsFinite(framePositionA) ||
                    !IsFinite(framePositionB) ||
                    !IsFinite(frameRotationA) ||
                    !IsFinite(frameRotationB))
                {
                    return false;
                }

                Vector3 visualPosition = Vector3.Lerp(framePositionA, framePositionB, Mathf.Clamp01(framePosition));
                Quaternion visualRotation = Quaternion.Slerp(frameRotationA, frameRotationB, Mathf.Clamp01(framePosition));
                if (IsFinite(visualPosition) &&
                    IsFinite(visualRotation) &&
                    (visualPosition - position).sqrMagnitude <= kVehicleCollisionFramePoseMaxDistanceSq)
                {
                    position = visualPosition;
                    rotation = LevelRotation(visualRotation);
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private bool TryGetVehicleCollisionShape(Entity vehicle, Vector3 position, Quaternion rotation, out VehicleCollisionShape shape)
        {
            shape = default;
            Vector3 size = ResolveCrashguardVehicleCollisionSize(vehicle);
            Vector3 localCenter = Vector3.zero;
            float minHeight = 0f;
            float maxHeight = Mathf.Max(kVehicleMeshSweepFallbackMaxHeight, Mathf.Abs(size.y));
            if (!m_FixedCollisionBoundsLogged)
            {
                Mod.log.Info("Direct Drive collision using fixed crashguard hitboxes; prefab mesh/geometry bounds are skipped during live driving.");
                m_FixedCollisionBoundsLogged = true;
            }

            Vector3 right = FlattenForward(rotation * Vector3.right);
            Vector3 shapeForward = FlattenForward(rotation * Vector3.forward);
            if (right.sqrMagnitude < 0.01f || shapeForward.sqrMagnitude < 0.01f)
                return false;

            float halfWidth = Mathf.Max(kVehicleCollisionMinHalfWidth, Mathf.Abs(size.x) * 0.5f * kVehicleCollisionWidthScale) + kVehicleCollisionSkin;
            float halfLength = Mathf.Max(kVehicleCollisionMinHalfLength, Mathf.Abs(size.z) * 0.5f * kVehicleCollisionLengthScale) + kVehicleCollisionSkin;
            Vector3 center = position + rotation * localCenter;
            shape = VehicleCollisionShape.Vehicle(center, right.normalized, shapeForward.normalized, halfWidth, halfLength, minHeight, maxHeight);
            return true;
        }

        private Vector3 ResolveCrashguardVehicleCollisionSize(Entity vehicle)
        {
            Entity prefab = Entity.Null;
            if (vehicle != Entity.Null &&
                EntityManager.Exists(vehicle) &&
                EntityManager.TryGetComponent(vehicle, out PrefabRef prefabRef))
            {
                prefab = prefabRef.m_Prefab;
                if (prefab != Entity.Null && m_VehicleCollisionSizeCache.TryGetValue(prefab, out Vector3 cached))
                    return cached;
            }

            Vector3 size = new Vector3(2.2f, 1.7f, 4.8f);
            string name = "";
            try
            {
                name = GetVehicleName(vehicle).ToLowerInvariant();
            }
            catch
            {
            }

            if (name.Contains("combine") || name.Contains("harvester"))
                size = new Vector3(3.6f, 3.0f, 9.5f);
            else if (name.Contains("tractor"))
                size = new Vector3(2.8f, 2.4f, 6.8f);
            else if (name.Contains("trailer"))
                size = new Vector3(2.8f, 2.6f, 11.5f);
            else if (name.Contains("bus") || name.Contains("truck") || name.Contains("garbage") || name.Contains("maintenance") || name.Contains("fire"))
                size = new Vector3(2.8f, 2.7f, 8.8f);
            else if (name.Contains("ambulance") || name.Contains("police"))
                size = new Vector3(2.4f, 2.2f, 5.8f);

            if (prefab != Entity.Null)
                m_VehicleCollisionSizeCache[prefab] = size;

            return size;
        }

        private static bool IsUsableVehicleBounds(Vector3 size, Vector3 center)
        {
            return IsFinite(size) &&
                   IsFinite(center) &&
                   Mathf.Abs(size.x) > 0.3f &&
                   Mathf.Abs(size.y) > 0.2f &&
                   Mathf.Abs(size.z) > 0.6f &&
                   Mathf.Abs(size.x) < 8f &&
                   Mathf.Abs(size.y) < 8f &&
                   Mathf.Abs(size.z) < 28f &&
                   Mathf.Abs(center.x) < 4f &&
                   Mathf.Abs(center.z) < 8f;
        }

        private bool TryGetPrefabMeshBounds(Entity prefab, float minHeight, out Bounds3 result)
        {
            result = default;
            if (!EntityManager.HasBuffer<SubMesh>(prefab))
                return false;

            DynamicBuffer<SubMesh> subMeshes = EntityManager.GetBuffer<SubMesh>(prefab, true);
            bool found = false;
            for (int i = 0; i < subMeshes.Length; i++)
            {
                SubMesh subMesh = subMeshes[i];
                if (!IsVehicleCollisionSubMeshVisible(subMesh) ||
                    !TryResolveRenderableMesh(subMesh.m_SubMesh, out Entity renderMesh))
                {
                    continue;
                }

                if (EntityManager.TryGetComponent(renderMesh, out MeshData meshData) &&
                    (meshData.m_State & MeshFlags.Decal) != (MeshFlags)0)
                {
                    continue;
                }

                if (!TryGetRenderMeshBounds(renderMesh, out Bounds3 meshBounds))
                    continue;

                Bounds3 localBounds = (subMesh.m_Flags & SubMeshFlags.HasTransform) != (SubMeshFlags)0
                    ? TransformLocalBounds(meshBounds, subMesh.m_Position, subMesh.m_Rotation)
                    : meshBounds;
                float3 size = localBounds.max - localBounds.min;
                if (!math.all(math.isfinite(size)) ||
                    size.x < 0.2f ||
                    size.z < 0.2f ||
                    size.y < minHeight)
                {
                    continue;
                }

                if (!found)
                {
                    result = localBounds;
                    found = true;
                }
                else
                {
                    result.min = math.min(result.min, localBounds.min);
                    result.max = math.max(result.max, localBounds.max);
                }
            }

            return found;
        }

        private bool IsVehicleCollisionSubMeshVisible(SubMesh subMesh)
        {
            if ((subMesh.m_Flags & SubMeshFlags.DefaultMissingMesh) != (SubMeshFlags)0)
                return false;

            if (m_CityConfigurationSystem == null)
                m_CityConfigurationSystem = World.GetOrCreateSystemManaged<CityConfigurationSystem>();

            bool leftHandTraffic = m_CityConfigurationSystem != null && m_CityConfigurationSystem.leftHandTraffic;
            SubMeshFlags visibleFlags = SubMeshFlags.HasTransform;
            visibleFlags |= leftHandTraffic ? SubMeshFlags.RequireLeftHandTraffic : SubMeshFlags.RequireRightHandTraffic;
            return (subMesh.m_Flags & visibleFlags) == subMesh.m_Flags;
        }

        private bool TryGetRenderMeshBounds(Entity renderMesh, out Bounds3 bounds)
        {
            bounds = default;
            if (renderMesh == Entity.Null || !EntityManager.Exists(renderMesh))
                return false;

            if (EntityManager.TryGetComponent(renderMesh, out MeshData meshData))
            {
                float3 size = meshData.m_Bounds.max - meshData.m_Bounds.min;
                if (math.all(math.isfinite(size)) &&
                    size.x > 0.001f &&
                    size.y > 0.001f &&
                    size.z > 0.001f)
                {
                    bounds = meshData.m_Bounds;
                    return true;
                }
            }

            if (!EntityManager.HasBuffer<MeshVertex>(renderMesh))
                return false;

            DynamicBuffer<MeshVertex> vertices = EntityManager.GetBuffer<MeshVertex>(renderMesh, true);
            if (vertices.Length == 0)
                return false;

            bounds.min = new float3(float.MaxValue, float.MaxValue, float.MaxValue);
            bounds.max = new float3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < vertices.Length; i++)
                EncapsulatePoint(ref bounds, vertices[i].m_Vertex);

            float3 vertexSize = bounds.max - bounds.min;
            return math.all(math.isfinite(vertexSize)) &&
                   vertexSize.x > 0.001f &&
                   vertexSize.y > 0.001f &&
                   vertexSize.z > 0.001f;
        }

        private bool TryResolveRenderableMesh(Entity mesh, out Entity renderMesh)
        {
            renderMesh = Entity.Null;
            if (mesh == Entity.Null || !EntityManager.Exists(mesh))
                return false;

            if (EntityManager.TryGetComponent(mesh, out SharedMeshData sharedMeshData))
                renderMesh = sharedMeshData.m_Mesh;
            else
                renderMesh = mesh;

            if (renderMesh != Entity.Null &&
                EntityManager.Exists(renderMesh) &&
                EntityManager.HasBuffer<MeshVertex>(renderMesh) &&
                EntityManager.HasBuffer<MeshIndex>(renderMesh))
            {
                return true;
            }

            if (!EntityManager.HasBuffer<LodMesh>(mesh))
            {
                renderMesh = Entity.Null;
                return false;
            }

            DynamicBuffer<LodMesh> lodMeshes = EntityManager.GetBuffer<LodMesh>(mesh, true);
            for (int i = lodMeshes.Length - 1; i >= 0; i--)
            {
                Entity lodMesh = lodMeshes[i].m_LodMesh;
                if (lodMesh == Entity.Null || !EntityManager.Exists(lodMesh))
                    continue;

                Entity candidate = EntityManager.TryGetComponent(lodMesh, out SharedMeshData lodSharedMesh)
                    ? lodSharedMesh.m_Mesh
                    : lodMesh;
                if (candidate != Entity.Null &&
                    EntityManager.Exists(candidate) &&
                    EntityManager.HasBuffer<MeshVertex>(candidate) &&
                    EntityManager.HasBuffer<MeshIndex>(candidate))
                {
                    renderMesh = candidate;
                    return true;
                }
            }

            renderMesh = Entity.Null;
            return false;
        }

        private static Bounds3 TransformLocalBounds(Bounds3 bounds, float3 position, quaternion rotation)
        {
            Bounds3 result;
            result.min = new float3(float.MaxValue, float.MaxValue, float.MaxValue);
            result.max = new float3(float.MinValue, float.MinValue, float.MinValue);
            EncapsulatePoint(ref result, position + math.mul(rotation, new float3(bounds.min.x, bounds.min.y, bounds.min.z)));
            EncapsulatePoint(ref result, position + math.mul(rotation, new float3(bounds.min.x, bounds.min.y, bounds.max.z)));
            EncapsulatePoint(ref result, position + math.mul(rotation, new float3(bounds.min.x, bounds.max.y, bounds.min.z)));
            EncapsulatePoint(ref result, position + math.mul(rotation, new float3(bounds.min.x, bounds.max.y, bounds.max.z)));
            EncapsulatePoint(ref result, position + math.mul(rotation, new float3(bounds.max.x, bounds.min.y, bounds.min.z)));
            EncapsulatePoint(ref result, position + math.mul(rotation, new float3(bounds.max.x, bounds.min.y, bounds.max.z)));
            EncapsulatePoint(ref result, position + math.mul(rotation, new float3(bounds.max.x, bounds.max.y, bounds.min.z)));
            EncapsulatePoint(ref result, position + math.mul(rotation, new float3(bounds.max.x, bounds.max.y, bounds.max.z)));
            return result;
        }

        private static void EncapsulatePoint(ref Bounds3 bounds, float3 point)
        {
            bounds.min = math.min(bounds.min, point);
            bounds.max = math.max(bounds.max, point);
        }

        private static bool IsSweptShapeNearShape(VehicleCollisionShape previousShape, VehicleCollisionShape currentShape, VehicleCollisionShape targetShape, float padding)
        {
            if (!SweptShapeOverlapsVertically(previousShape, currentShape, targetShape, kVehicleCollisionVerticalPadding))
                return false;

            Vector3 sweepCenter = (previousShape.Body.Center + currentShape.Body.Center) * 0.5f;
            Vector3 sweepDelta = currentShape.Body.Center - previousShape.Body.Center;
            sweepDelta.y = 0f;
            Vector3 targetDelta = targetShape.Body.Center - sweepCenter;
            targetDelta.y = 0f;

            float sweepRadius = currentShape.MaxRadius + sweepDelta.magnitude * 0.5f + Mathf.Max(0f, padding);
            float combinedRadius = sweepRadius + targetShape.MaxRadius;
            return targetDelta.sqrMagnitude <= combinedRadius * combinedRadius;
        }

        private static bool TrySweepVehicleBumper(VehicleCollisionShape previousSelfShape, VehicleCollisionShape selfShape, VehicleCollisionShape targetShape, Vector3 forward, float speedMps, out float hitT, out Vector3 normal)
        {
            hitT = 2f;
            normal = Vector3.zero;
            if (!SweptShapeOverlapsVertically(previousSelfShape, selfShape, targetShape, kVehicleCollisionVerticalPadding))
                return false;

            bool reversing = speedMps < -0.05f;
            Vector3 bumperForward = reversing ? -forward : forward;
            Vector3 right = selfShape.Body.Right;
            float sideProbeOffset = Mathf.Max(0f, selfShape.Body.HalfWidth - kVehicleBumperSweepSideInset);

            Vector3 startCenter = previousSelfShape.Body.Center + bumperForward * previousSelfShape.Body.HalfLength;
            Vector3 endCenter = selfShape.Body.Center + bumperForward * selfShape.Body.HalfLength;
            bool hit = false;
            TrySweepBumperProbe(startCenter, endCenter, targetShape.Body, ref hit, ref hitT, ref normal);

            if (sideProbeOffset > 0.05f)
            {
                Vector3 side = right * sideProbeOffset;
                TrySweepBumperProbe(startCenter + side, endCenter + side, targetShape.Body, ref hit, ref hitT, ref normal);
                TrySweepBumperProbe(startCenter - side, endCenter - side, targetShape.Body, ref hit, ref hitT, ref normal);
            }

            return hit;
        }

        private static bool SweptShapeOverlapsVertically(VehicleCollisionShape previousShape, VehicleCollisionShape currentShape, VehicleCollisionShape targetShape, float padding)
        {
            float selfMin = Mathf.Min(previousShape.Body.Center.y + previousShape.MinHeight, currentShape.Body.Center.y + currentShape.MinHeight) - padding;
            float selfMax = Mathf.Max(previousShape.Body.Center.y + previousShape.MaxHeight, currentShape.Body.Center.y + currentShape.MaxHeight) + padding;
            float targetMin = targetShape.Body.Center.y + targetShape.MinHeight - padding;
            float targetMax = targetShape.Body.Center.y + targetShape.MaxHeight + padding;
            return selfMin <= targetMax && targetMin <= selfMax;
        }

        private static void TrySweepBumperProbe(Vector3 start, Vector3 end, VehicleCollisionBox target, ref bool hit, ref float bestT, ref Vector3 bestNormal)
        {
            if (!TrySweepPointAgainstBoxXZ(start, end, target, kVehicleBumperSweepProbeRadius, out float probeT, out Vector3 probeNormal) ||
                probeT >= bestT)
            {
                return;
            }

            hit = true;
            bestT = probeT;
            bestNormal = probeNormal;
        }

        private static bool TrySweepPointAgainstBoxXZ(Vector3 start, Vector3 end, VehicleCollisionBox box, float padding, out float hitT, out Vector3 normal)
        {
            hitT = 0f;
            normal = Vector3.zero;

            Vector2 startLocal = ToBoxLocalXZ(start, box);
            Vector2 endLocal = ToBoxLocalXZ(end, box);
            Vector2 delta = endLocal - startLocal;
            float halfWidth = box.HalfWidth + padding;
            float halfLength = box.HalfLength + padding;

            if (Mathf.Abs(startLocal.x) <= halfWidth &&
                Mathf.Abs(startLocal.y) <= halfLength)
            {
                float widthClearance = halfWidth - Mathf.Abs(startLocal.x);
                float lengthClearance = halfLength - Mathf.Abs(startLocal.y);
                normal = widthClearance < lengthClearance
                    ? (startLocal.x >= 0f ? box.Right : -box.Right)
                    : (startLocal.y >= 0f ? box.Forward : -box.Forward);
                return true;
            }

            float tMin = 0f;
            float tMax = 1f;
            Vector3 entryNormal = Vector3.zero;
            if (!ClipSweepAxis(startLocal.x, delta.x, -halfWidth, halfWidth, -box.Right, box.Right, ref tMin, ref tMax, ref entryNormal) ||
                !ClipSweepAxis(startLocal.y, delta.y, -halfLength, halfLength, -box.Forward, box.Forward, ref tMin, ref tMax, ref entryNormal))
            {
                return false;
            }

            if (tMin < 0f || tMin > 1f)
                return false;

            hitT = tMin;
            normal = entryNormal.sqrMagnitude > 0.0001f ? entryNormal.normalized : -FlattenForward(end - start).normalized;
            return normal.sqrMagnitude > 0.0001f;
        }

        private static bool ClipSweepAxis(float start, float delta, float min, float max, Vector3 minNormal, Vector3 maxNormal, ref float tMin, ref float tMax, ref Vector3 entryNormal)
        {
            if (Mathf.Abs(delta) < 0.00001f)
                return start >= min && start <= max;

            float invDelta = 1f / delta;
            float t1 = (min - start) * invDelta;
            float t2 = (max - start) * invDelta;
            Vector3 normal1 = minNormal;
            Vector3 normal2 = maxNormal;
            if (t1 > t2)
            {
                float tempT = t1;
                t1 = t2;
                t2 = tempT;
                Vector3 tempNormal = normal1;
                normal1 = normal2;
                normal2 = tempNormal;
            }

            if (t1 > tMin)
            {
                tMin = t1;
                entryNormal = normal1;
            }

            if (t2 < tMax)
                tMax = t2;

            return tMin <= tMax;
        }

        private static Vector2 ToBoxLocalXZ(Vector3 point, VehicleCollisionBox box)
        {
            Vector3 delta = point - box.Center;
            return new Vector2(Vector3.Dot(delta, box.Right), Vector3.Dot(delta, box.Forward));
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsInfinity(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsNaN(value.w) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsInfinity(value.z) &&
                   !float.IsInfinity(value.w);
        }

        private static float ClampFinite(float value, float fallback, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return fallback;

            return Mathf.Clamp(value, min, max);
        }

        private struct VehicleCollisionBox
        {
            internal Vector3 Center;
            internal Vector3 Right;
            internal Vector3 Forward;
            internal float HalfWidth;
            internal float HalfLength;
        }

        private struct VehicleCollisionShape
        {
            internal VehicleCollisionBox Body;
            internal VehicleCollisionBox Main;
            internal VehicleCollisionBox Front;
            internal VehicleCollisionBox Rear;
            internal int Count;
            internal float MaxRadius;
            internal float MinHeight;
            internal float MaxHeight;

            internal static VehicleCollisionShape Vehicle(Vector3 center, Vector3 right, Vector3 forward, float halfWidth, float halfLength, float minHeight, float maxHeight)
            {
                VehicleCollisionBox body = new VehicleCollisionBox
                {
                    Center = center,
                    Right = right,
                    Forward = forward,
                    HalfWidth = halfWidth,
                    HalfLength = halfLength
                };
                VehicleCollisionBox main = new VehicleCollisionBox
                {
                    Center = center,
                    Right = right,
                    Forward = forward,
                    HalfWidth = halfWidth,
                    HalfLength = Mathf.Max(kVehicleCollisionMinHalfLength * 0.55f, halfLength * kVehicleCollisionMiddleLengthScale)
                };
                VehicleCollisionBox front = new VehicleCollisionBox
                {
                    Center = center + forward * (halfLength * kVehicleCollisionEndOffsetScale),
                    Right = right,
                    Forward = forward,
                    HalfWidth = Mathf.Max(kVehicleCollisionMinHalfWidth * 0.75f, halfWidth * kVehicleCollisionEndWidthScale),
                    HalfLength = Mathf.Max(0.45f, halfLength * kVehicleCollisionEndLengthScale)
                };
                VehicleCollisionBox rear = front;
                rear.Center = center - forward * (halfLength * kVehicleCollisionEndOffsetScale);

                return new VehicleCollisionShape
                {
                    Body = body,
                    Main = main,
                    Front = front,
                    Rear = rear,
                    Count = 3,
                    MaxRadius = Mathf.Sqrt(halfWidth * halfWidth + halfLength * halfLength),
                    MinHeight = minHeight,
                    MaxHeight = maxHeight
                };
            }
        }
    }
}
