using Colossal.Mathematics;
using Colossal.Entities;
using Game;
using Game.Common;
using Game.Objects;
using Game.Tools;
using Game.Vehicles;
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ObjectTransform = Game.Objects.Transform;

namespace BetaTestDrivingMod
{
    public sealed partial class DirectDriveTrafficPresenceSystem : GameSystemBase
    {
        private Entity m_LastCar = Entity.Null;
        private Entity m_LastLane = Entity.Null;
        private Entity m_LastChangeLane = Entity.Null;
        private readonly List<Entity> m_LastHaloLanes = new List<Entity>(6);
        private readonly List<Entity> m_CurrentHaloLanes = new List<Entity>(6);
        private int m_LogCooldown;
        private string m_LastHaloStatus = "halo pending";

        protected override void OnUpdate()
        {
            try
            {
                if (!DirectDriveRuntime.IsDriving ||
                    !DirectDriveRuntime.RoadIntentAssist ||
                    !DirectDriveRuntime.HasTrafficPresenceTarget)
                {
                    ClearLastPresence();
                    return;
                }

                Entity car = DirectDriveRuntime.PossessedEntity;
                Entity lane = DirectDriveRuntime.TrafficPresenceLane;
                Entity changeLane = DirectDriveRuntime.TrafficPresenceHaloEnabled
                    ? DirectDriveRuntime.TrafficPresenceChangeLane
                    : Entity.Null;
                if (!IsLivePossessedCar(car) ||
                    lane == Entity.Null ||
                    !EntityManager.Exists(lane) ||
                    !EntityManager.HasBuffer<Game.Net.LaneObject>(lane))
                {
                    ClearLastPresence();
                    return;
                }

                float2 curvePosition = BuildCurrentCurvePosition(car, lane);
                if (!math.all(math.isfinite(curvePosition)))
                {
                    DirectDriveRuntime.ClearTrafficPresenceDebug();
                    return;
                }

                if (m_LastLane != Entity.Null && m_LastLane != lane)
                    RemoveLaneObject(m_LastLane, car);

                if (m_LastChangeLane != Entity.Null && m_LastChangeLane != changeLane)
                    RemoveLaneObject(m_LastChangeLane, car);

                UpsertLaneObject(lane, car, curvePosition);
                if (changeLane != Entity.Null && changeLane != lane)
                    UpsertLaneObject(changeLane, car, BuildLaneCurvePosition(car, changeLane, 1f, 1f));

                SyncNearbyLaneHalo(car, lane, changeLane);
                RecordTrafficPresenceDebug(car, lane, changeLane, curvePosition);

                m_LastCar = car;
                m_LastLane = lane;
                m_LastChangeLane = changeLane;
            }
            catch (Exception ex)
            {
                if (m_LogCooldown-- <= 0)
                {
                    Mod.log.Warn($"Direct Drive pre-move traffic presence skipped after {ex.GetType().Name}: {ex.Message}");
                    m_LogCooldown = 180;
                }

                DirectDriveRuntime.ClearTrafficPresenceDebug();
            }
        }

        private bool IsLivePossessedCar(Entity car)
        {
            return car != Entity.Null &&
                   EntityManager.Exists(car) &&
                   EntityManager.HasComponent<Car>(car) &&
                   EntityManager.HasComponent<ObjectTransform>(car) &&
                   !EntityManager.HasComponent<Deleted>(car) &&
                   !EntityManager.HasComponent<Temp>(car);
        }

        private float2 BuildCurrentCurvePosition(Entity car, Entity lane)
        {
            return BuildLaneCurvePosition(car, lane, 1f, 1f);
        }

        private float2 BuildLaneCurvePosition(Entity car, Entity lane, float rearScale, float forwardScale)
        {
            float curveT = DirectDriveRuntime.TrafficPresenceCurveT;
            if (EntityManager.TryGetComponent(car, out ObjectTransform transform) &&
                EntityManager.TryGetComponent(lane, out Game.Net.Curve curve) &&
                curve.m_Length > 1f)
            {
                MathUtils.Distance(curve.m_Bezier, transform.m_Position, out curveT);
                curveT = math.saturate(curveT);
            }

            float curveSign = DirectDriveRuntime.TrafficPresenceCurveSign < 0f ? -1f : 1f;
            float rearSpan = math.max(0.0001f, DirectDriveRuntime.TrafficPresenceRearSpan * math.max(0.05f, rearScale));
            float forwardSpan = math.max(0.0001f, DirectDriveRuntime.TrafficPresenceForwardSpan * math.max(0.05f, forwardScale));
            float start = math.saturate(curveT + (curveSign < 0f ? rearSpan : -rearSpan));
            float end = math.saturate(curveT + (curveSign < 0f ? -forwardSpan : forwardSpan));
            return new float2(start, end);
        }

        private void ClearLastPresence()
        {
            Entity car = m_LastCar != Entity.Null ? m_LastCar : DirectDriveRuntime.PossessedEntity;
            if (car == Entity.Null)
            {
                m_LastCar = Entity.Null;
                m_LastLane = Entity.Null;
                m_LastChangeLane = Entity.Null;
                m_LastHaloLanes.Clear();
                m_CurrentHaloLanes.Clear();
                m_LastHaloStatus = "extra halo cleared";
                DirectDriveRuntime.ClearTrafficPresenceDebug();
                return;
            }

            if (m_LastLane != Entity.Null)
                RemoveLaneObject(m_LastLane, car);

            if (m_LastChangeLane != Entity.Null && m_LastChangeLane != m_LastLane)
                RemoveLaneObject(m_LastChangeLane, car);

            for (int i = 0; i < m_LastHaloLanes.Count; i++)
            {
                Entity lane = m_LastHaloLanes[i];
                if (lane != m_LastLane && lane != m_LastChangeLane)
                    RemoveLaneObject(lane, car);
            }

            m_LastCar = Entity.Null;
            m_LastLane = Entity.Null;
            m_LastChangeLane = Entity.Null;
            m_LastHaloLanes.Clear();
            m_CurrentHaloLanes.Clear();
            m_LastHaloStatus = "extra halo cleared";
            DirectDriveRuntime.ClearTrafficPresenceDebug();
        }

        private void SyncNearbyLaneHalo(Entity car, Entity primaryLane, Entity changeLane)
        {
            m_CurrentHaloLanes.Clear();
            if (!DirectDriveRuntime.TrafficPresenceHaloEnabled)
            {
                m_LastHaloStatus = "halo disabled";
                ClearHaloPresence(car, primaryLane, changeLane);
                return;
            }

            if (changeLane != Entity.Null && changeLane != primaryLane)
            {
                m_CurrentHaloLanes.Add(changeLane);
                m_LastHaloStatus = $"halo active {changeLane.Index}";
            }
            else
            {
                m_LastHaloStatus = DirectDriveRuntime.RoadOnlyTrafficPresence ? "halo road-only" : "halo standby";
            }

            RemoveStaleHaloLanes(car, primaryLane, changeLane);
            m_LastHaloLanes.Clear();
            for (int i = 0; i < m_CurrentHaloLanes.Count; i++)
                m_LastHaloLanes.Add(m_CurrentHaloLanes[i]);
        }

        private void ClearHaloPresence(Entity car, Entity primaryLane, Entity changeLane)
        {
            m_CurrentHaloLanes.Clear();
            RemoveStaleHaloLanes(car, primaryLane, changeLane);
            m_LastHaloLanes.Clear();
        }

        private void RecordTrafficPresenceDebug(Entity car, Entity primaryLane, Entity changeLane, float2 primaryCurvePosition)
        {
            if (!DirectDriveRuntime.TrafficPresenceDebugEnabled)
            {
                DirectDriveRuntime.ClearTrafficPresenceDebug();
                return;
            }

            string changeText = changeLane != Entity.Null ? changeLane.Index.ToString() : "none";
            DirectDriveRuntime.BeginTrafficPresenceDebug($"primary {primaryLane.Index}, halo {changeText}, {m_LastHaloStatus}");

            AddTrafficPresenceDebugSegment(primaryLane, primaryCurvePosition, $"PRIMARY {primaryLane.Index}", DirectDriveRuntime.kTrafficPresenceDebugPrimary);

            if (changeLane != Entity.Null && changeLane != primaryLane)
            {
                float2 haloCurvePosition = BuildLaneCurvePosition(car, changeLane, 1f, 1f);
                AddTrafficPresenceDebugSegment(changeLane, haloCurvePosition, $"HALO {changeLane.Index}", DirectDriveRuntime.kTrafficPresenceDebugHalo);
            }

        }

        private void AddTrafficPresenceDebugSegment(Entity lane, float2 curvePosition, string label, int kind)
        {
            if (lane == Entity.Null ||
                !EntityManager.TryGetComponent(lane, out Game.Net.Curve curve) ||
                curve.m_Length < 1f ||
                !math.all(math.isfinite(curvePosition)))
            {
                return;
            }

            float startT = math.saturate(curvePosition.x);
            float endT = math.saturate(curvePosition.y);
            float labelT = math.saturate((startT + endT) * 0.5f);
            Vector3 lift = Vector3.up * 0.32f;
            Vector3 start = ToUnityVector(MathUtils.Position(curve.m_Bezier, startT)) + lift;
            Vector3 end = ToUnityVector(MathUtils.Position(curve.m_Bezier, endT)) + lift;
            Vector3 labelPosition = ToUnityVector(MathUtils.Position(curve.m_Bezier, labelT)) + Vector3.up * 1.15f;
            DirectDriveRuntime.AddTrafficPresenceDebugSegment(start, end, labelPosition, label, kind);
        }

        private static Vector3 ToUnityVector(float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private void RemoveStaleHaloLanes(Entity car, Entity primaryLane, Entity changeLane)
        {
            for (int i = 0; i < m_LastHaloLanes.Count; i++)
            {
                Entity lane = m_LastHaloLanes[i];
                if (lane != primaryLane &&
                    lane != changeLane &&
                    !ContainsEntity(m_CurrentHaloLanes, lane))
                {
                    RemoveLaneObject(lane, car);
                }
            }
        }

        private static bool ContainsEntity(List<Entity> entities, Entity entity)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i] == entity)
                    return true;
            }

            return false;
        }

        private bool UpsertLaneObject(Entity lane, Entity laneObject, float2 curvePosition)
        {
            if (lane == Entity.Null ||
                laneObject == Entity.Null ||
                !EntityManager.Exists(lane) ||
                !EntityManager.HasBuffer<Game.Net.LaneObject>(lane))
            {
                return false;
            }

            DynamicBuffer<Game.Net.LaneObject> laneObjects = EntityManager.GetBuffer<Game.Net.LaneObject>(lane);
            Game.Net.NetUtils.UpdateLaneObject(laneObjects, laneObject, curvePosition);
            return true;
        }

        private bool RemoveLaneObject(Entity lane, Entity laneObject)
        {
            if (lane == Entity.Null ||
                laneObject == Entity.Null ||
                !EntityManager.Exists(lane) ||
                !EntityManager.HasBuffer<Game.Net.LaneObject>(lane))
            {
                return false;
            }

            DynamicBuffer<Game.Net.LaneObject> laneObjects = EntityManager.GetBuffer<Game.Net.LaneObject>(lane);
            int before = laneObjects.Length;
            Game.Net.NetUtils.RemoveLaneObject(laneObjects, laneObject);
            return laneObjects.Length != before;
        }
    }
}
