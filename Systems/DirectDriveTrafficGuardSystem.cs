using Colossal.Entities;
using Game;
using Game.Common;
using Game.Objects;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ObjectTransform = Game.Objects.Transform;

namespace BetaTestDrivingMod
{
    public sealed partial class DirectDriveTrafficGuardSystem : GameSystemBase
    {
        private const float kGuardScanRadius = 12f;
        private const float kGuardActionRadius = 6.2f;
        private const float kGuardMaxHeightDelta = 2.4f;
        private const float kGuardVehicleHalfWidth = 0.85f;
        private const float kGuardVehicleHalfLength = 1.95f;
        private const float kGuardLateralPadding = 0.02f;
        private const float kGuardLongitudinalPadding = 0.06f;
        private const float kGuardMinSpeedMps = 0.4f;
        private const float kGuardMinClosingMps = 0.35f;
        private const float kGuardMinRelativeClosingMps = 0.25f;
        private const float kGuardBrakeExtraMps = 0.12f;
        private const float kGuardMinLookAheadSeconds = 0.12f;
        private const float kGuardBaseLookAheadSeconds = 0.16f;
        private const float kGuardLookAheadPerMps = 0.004f;
        private const float kGuardMaxLookAheadSeconds = 0.28f;
        private const uint kGuardCandidateRefreshFrames = 6U;
        private const float kGuardCandidateRefreshDistanceSq = 36f;

        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_TrafficQuery;
        private readonly List<Entity> m_CandidateCache = new List<Entity>(128);
        private Vector3 m_CacheCenter;
        private uint m_CacheFrame;
        private bool m_CacheValid;
        private int m_LogCooldown;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TrafficQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Car>(),
                    ComponentType.ReadWrite<Moving>(),
                    ComponentType.ReadOnly<ObjectTransform>()
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

        protected override void OnUpdate()
        {
            try
            {
                OnUpdateSafe();
            }
            catch (Exception ex)
            {
                if (m_LogCooldown-- <= 0)
                {
                    Mod.log.Warn($"Direct Drive traffic guard skipped after {ex.GetType().Name}: {ex.Message}");
                    m_LogCooldown = 180;
                }

                DirectDriveRuntime.ClearTrafficGuardDebug();
            }
        }

        private void OnUpdateSafe()
        {
            if (!DirectDriveRuntime.IsDriving ||
                !DirectDriveRuntime.VehicleCollisionEnabled)
            {
                DirectDriveRuntime.ClearTrafficGuardDebug();
                m_CacheValid = false;
                return;
            }

            Entity possessed = DirectDriveRuntime.PossessedEntity;
            if (possessed == Entity.Null ||
                !EntityManager.Exists(possessed) ||
                !EntityManager.TryGetComponent(possessed, out ObjectTransform selfTransform) ||
                !EntityManager.TryGetComponent(possessed, out Moving selfMoving))
            {
                DirectDriveRuntime.ClearTrafficGuardDebug();
                m_CacheValid = false;
                return;
            }

            Vector3 selfPosition = ToUnityVector(selfTransform.m_Position);
            Quaternion selfRotation = LevelRotation(ToUnityQuaternion(selfTransform.m_Rotation));
            Vector3 selfVelocity = Flatten(ToUnityVector(selfMoving.m_Velocity));
            if (!IsFinite(selfPosition) || !IsFinite(selfRotation) || !IsFinite(selfVelocity))
            {
                DirectDriveRuntime.ClearTrafficGuardDebug();
                return;
            }

            GuardBox selfBox = BuildGuardBox(selfPosition, selfRotation);
            RefreshCandidateCache(possessed, selfPosition);

            int blocked = 0;
            Entity closest = Entity.Null;
            float closestDistanceSq = float.MaxValue;
            float closestClosing = 0f;
            for (int i = 0; i < m_CandidateCache.Count; i++)
            {
                Entity candidate = m_CandidateCache[i];
                if (candidate == possessed ||
                    candidate == Entity.Null ||
                    !EntityManager.Exists(candidate))
                {
                    continue;
                }

                if (TryBrakeClosingTraffic(candidate, selfBox, selfVelocity, out float distanceSq, out float closingSpeed))
                {
                    blocked++;
                    if (distanceSq < closestDistanceSq)
                    {
                        closest = candidate;
                        closestDistanceSq = distanceSq;
                        closestClosing = closingSpeed;
                    }
                }
            }

            if (blocked > 0)
                DirectDriveRuntime.SetTrafficGuardDebug($"guard braking {blocked} car(s), nearest {closest.Index} closing {closestClosing:0.0}m/s");
            else
                DirectDriveRuntime.SetTrafficGuardDebug("guard clear");
        }

        private void RefreshCandidateCache(Entity possessed, Vector3 position)
        {
            uint frame = m_SimulationSystem != null ? m_SimulationSystem.frameIndex : 0U;
            bool expired = !m_CacheValid ||
                frame == 0U ||
                frame - m_CacheFrame >= kGuardCandidateRefreshFrames ||
                (position - m_CacheCenter).sqrMagnitude > kGuardCandidateRefreshDistanceSq;
            if (!expired)
                return;

            m_CandidateCache.Clear();
            m_CacheCenter = position;
            m_CacheFrame = frame;
            m_CacheValid = true;

            if (m_TrafficQuery == default || m_TrafficQuery.IsEmptyIgnoreFilter)
                return;

            float scanRadiusSq = kGuardScanRadius * kGuardScanRadius;
            NativeArray<Entity> entities = m_TrafficQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity candidate = entities[i];
                    if (candidate == possessed ||
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
                    if (delta.sqrMagnitude <= scanRadiusSq)
                        m_CandidateCache.Add(candidate);
                }
            }
            finally
            {
                entities.Dispose();
            }
        }

        private bool TryBrakeClosingTraffic(Entity candidate, GuardBox selfBox, Vector3 selfVelocity, out float distanceSq, out float closingSpeed)
        {
            distanceSq = float.MaxValue;
            closingSpeed = 0f;
            if (!EntityManager.TryGetComponent(candidate, out ObjectTransform transform) ||
                !EntityManager.TryGetComponent(candidate, out Moving moving))
            {
                return false;
            }

            Vector3 position = ToUnityVector(transform.m_Position);
            Vector3 velocity = ToUnityVector(moving.m_Velocity);
            if (!IsFinite(position) || !IsFinite(velocity))
                return false;

            Vector3 flatVelocity = Flatten(velocity);
            float speed = flatVelocity.magnitude;
            if (speed < kGuardMinSpeedMps)
                return false;

            Vector3 toSelf = selfBox.Center - position;
            float heightDelta = Mathf.Abs(toSelf.y);
            toSelf.y = 0f;
            distanceSq = toSelf.sqrMagnitude;
            if (heightDelta > kGuardMaxHeightDelta ||
                distanceSq > kGuardActionRadius * kGuardActionRadius ||
                distanceSq < 0.0001f)
            {
                return false;
            }

            Vector3 toSelfDir = toSelf.normalized;
            float directClosing = Vector3.Dot(flatVelocity, toSelfDir);
            if (directClosing < kGuardMinClosingMps)
                return false;

            Vector3 relativeVelocity = flatVelocity - selfVelocity;
            float relativeClosing = Vector3.Dot(relativeVelocity, toSelfDir);
            ExpandedGuardBox expanded = ExpandForTraffic(selfBox);
            Vector2 localNow = ToBoxLocalXZ(position, expanded);
            if (Mathf.Abs(localNow.x) > expanded.HalfWidth)
                return false;

            float trafficAlongSelfForward = Vector3.Dot(flatVelocity, selfBox.Forward);
            if (localNow.y > expanded.HalfLength + 0.25f &&
                trafficAlongSelfForward > -0.1f)
            {
                return false;
            }

            bool alreadyInBox = IsPointInsideBoxXZ(position, expanded);
            if (relativeClosing < kGuardMinRelativeClosingMps && !alreadyInBox)
                return false;

            float horizon = Mathf.Clamp(kGuardBaseLookAheadSeconds + speed * kGuardLookAheadPerMps, kGuardMinLookAheadSeconds, kGuardMaxLookAheadSeconds);
            Vector3 relativeEnd = position + relativeVelocity * horizon;
            if (!alreadyInBox && !TrySweepPointAgainstBoxXZ(position, relativeEnd, expanded))
                return false;

            Vector3 newVelocity = flatVelocity;
            if (relativeClosing > 0f)
                newVelocity -= toSelfDir * (relativeClosing + kGuardBrakeExtraMps);

            float remainingClosing = Vector3.Dot(newVelocity - selfVelocity, toSelfDir);
            if (remainingClosing > 0f)
                newVelocity -= toSelfDir * remainingClosing;

            if (newVelocity.sqrMagnitude < 0.09f)
                newVelocity = Vector3.zero;

            velocity.x = newVelocity.x;
            velocity.z = newVelocity.z;
            moving.m_Velocity = ToMathVector(velocity);

            EntityManager.SetComponentData(candidate, moving);
            closingSpeed = Mathf.Max(directClosing, relativeClosing);
            return true;
        }

        private static GuardBox BuildGuardBox(Vector3 center, Quaternion rotation)
        {
            Vector3 right = Flatten(rotation * Vector3.right);
            Vector3 forward = Flatten(rotation * Vector3.forward);
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            return new GuardBox
            {
                Center = center,
                Right = right.normalized,
                Forward = forward.normalized,
                HalfWidth = kGuardVehicleHalfWidth,
                HalfLength = kGuardVehicleHalfLength
            };
        }

        private static ExpandedGuardBox ExpandForTraffic(GuardBox box)
        {
            return new ExpandedGuardBox
            {
                Center = box.Center,
                Right = box.Right,
                Forward = box.Forward,
                HalfWidth = box.HalfWidth + kGuardVehicleHalfWidth + kGuardLateralPadding,
                HalfLength = box.HalfLength + kGuardVehicleHalfLength + kGuardLongitudinalPadding
            };
        }

        private static bool TrySweepPointAgainstBoxXZ(Vector3 start, Vector3 end, ExpandedGuardBox box)
        {
            Vector2 startLocal = ToBoxLocalXZ(start, box);
            Vector2 endLocal = ToBoxLocalXZ(end, box);
            Vector2 delta = endLocal - startLocal;
            if (Mathf.Abs(startLocal.x) <= box.HalfWidth &&
                Mathf.Abs(startLocal.y) <= box.HalfLength)
            {
                return true;
            }

            float tMin = 0f;
            float tMax = 1f;
            return ClipSweepAxis(startLocal.x, delta.x, -box.HalfWidth, box.HalfWidth, ref tMin, ref tMax) &&
                   ClipSweepAxis(startLocal.y, delta.y, -box.HalfLength, box.HalfLength, ref tMin, ref tMax) &&
                   tMin <= tMax &&
                   tMin >= 0f &&
                   tMin <= 1f;
        }

        private static bool IsPointInsideBoxXZ(Vector3 point, ExpandedGuardBox box)
        {
            Vector2 local = ToBoxLocalXZ(point, box);
            return Mathf.Abs(local.x) <= box.HalfWidth &&
                   Mathf.Abs(local.y) <= box.HalfLength;
        }

        private static bool ClipSweepAxis(float start, float delta, float min, float max, ref float tMin, ref float tMax)
        {
            if (Mathf.Abs(delta) < 0.00001f)
                return start >= min && start <= max;

            float invDelta = 1f / delta;
            float t1 = (min - start) * invDelta;
            float t2 = (max - start) * invDelta;
            if (t1 > t2)
            {
                float swap = t1;
                t1 = t2;
                t2 = swap;
            }

            if (t1 > tMin)
                tMin = t1;
            if (t2 < tMax)
                tMax = t2;

            return tMin <= tMax;
        }

        private static Vector2 ToBoxLocalXZ(Vector3 point, ExpandedGuardBox box)
        {
            Vector3 delta = point - box.Center;
            return new Vector2(Vector3.Dot(delta, box.Right), Vector3.Dot(delta, box.Forward));
        }

        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value.sqrMagnitude > 0.0001f ? value : Vector3.zero;
        }

        private static Quaternion LevelRotation(Quaternion rotation)
        {
            Vector3 forward = Flatten(rotation * Vector3.forward);
            return forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
        }

        private static Vector3 ToUnityVector(float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static float3 ToMathVector(Vector3 value)
        {
            return new float3(value.x, value.y, value.z);
        }

        private static Quaternion ToUnityQuaternion(quaternion value)
        {
            return new Quaternion(value.value.x, value.value.y, value.value.z, value.value.w);
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

        private struct GuardBox
        {
            internal Vector3 Center;
            internal Vector3 Right;
            internal Vector3 Forward;
            internal float HalfWidth;
            internal float HalfLength;
        }

        private struct ExpandedGuardBox
        {
            internal Vector3 Center;
            internal Vector3 Right;
            internal Vector3 Forward;
            internal float HalfWidth;
            internal float HalfLength;
        }
    }
}
