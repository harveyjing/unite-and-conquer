using System;
using System.Globalization;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;

namespace Demo
{
    // Bridges the ECS client world to the demo HUD UIDocument.
    //
    // OnEnable: instantiate the view model, set it as the
    // rootVisualElement.dataSource, and wire button click callbacks.
    // Update: lazy-find the client world, cache four EntityQueries,
    // poll values each frame, and write formatted strings into the
    // view model. Setters short-circuit unchanged values, so the
    // bindings only push to the labels when state actually changes.
    // OnDisable: unregister callbacks, dispose queries.
    [RequireComponent(typeof(UIDocument))]
    public class DemoHudController : MonoBehaviour
    {
        DemoHudViewModel _viewModel;
        Button _respawnBtn;
        Button _spawnObstacleBtn;
        EventCallback<ClickEvent> _respawnHandler;
        EventCallback<ClickEvent> _spawnObstacleHandler;

        World _clientWorld;
        EntityQuery _localPlayerQuery;
        EntityQuery _ghostQuery;
        EntityQuery _networkIdQuery;
        EntityQuery _networkTimeQuery;

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            var root = doc.rootVisualElement;
            if (root == null) return;

            _viewModel = new DemoHudViewModel();
            root.dataSource = _viewModel;

            _respawnBtn       = root.Q<Button>("respawn-btn");
            _spawnObstacleBtn = root.Q<Button>("spawn-obstacle-btn");

            _respawnHandler       = _ => SendRpc<RespawnRequest>();
            _spawnObstacleHandler = _ => SendRpc<SpawnObstacleRequest>();

            _respawnBtn?.RegisterCallback(_respawnHandler);
            _spawnObstacleBtn?.RegisterCallback(_spawnObstacleHandler);
        }

        void OnDisable()
        {
            if (_respawnBtn != null && _respawnHandler != null)
                _respawnBtn.UnregisterCallback(_respawnHandler);
            if (_spawnObstacleBtn != null && _spawnObstacleHandler != null)
                _spawnObstacleBtn.UnregisterCallback(_spawnObstacleHandler);

            DisposeQueries();
            _clientWorld = null;
        }

        void DisposeQueries()
        {
            if (_localPlayerQuery  != default) _localPlayerQuery.Dispose();
            if (_ghostQuery        != default) _ghostQuery.Dispose();
            if (_networkIdQuery    != default) _networkIdQuery.Dispose();
            if (_networkTimeQuery  != default) _networkTimeQuery.Dispose();
            _localPlayerQuery = default;
            _ghostQuery = default;
            _networkIdQuery = default;
            _networkTimeQuery = default;
        }

        // Finds the first world whose name contains "client" (case-insensitive).
        // Returns null if no client world exists yet (e.g. before bootstrap).
        static World FindClientWorld()
        {
            foreach (var w in World.All)
                if (w.Name.IndexOf("client", StringComparison.OrdinalIgnoreCase) >= 0)
                    return w;
            return null;
        }

        void Update()
        {
            if (_viewModel == null) return;

            if (_clientWorld == null || !_clientWorld.IsCreated)
            {
                DisposeQueries();
                _clientWorld = FindClientWorld();
                if (_clientWorld == null) return;

                var em = _clientWorld.EntityManager;
                _localPlayerQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>(),
                    ComponentType.ReadOnly<PlayerTag>());
                _ghostQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GhostInstance>());
                _networkIdQuery = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
                _networkTimeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkTime>());
            }

            // Connection
            if (!_networkIdQuery.IsEmpty)
            {
                var id = _networkIdQuery.GetSingleton<NetworkId>();
                _viewModel.ConnectionText = $"Connected as Client #{id.Value}";
            }
            else
            {
                _viewModel.ConnectionText = "Disconnected";
            }

            // Position (local player)
            if (!_localPlayerQuery.IsEmpty)
            {
                var t = _localPlayerQuery.GetSingleton<LocalTransform>();
                _viewModel.PositionText = string.Format(
                    CultureInfo.InvariantCulture,
                    "Pos: ({0:0.0}, {1:0.0}, {2:0.0})",
                    t.Position.x, t.Position.y, t.Position.z);
            }
            else
            {
                _viewModel.PositionText = "Pos: -";
            }

            // Ghost count
            _viewModel.GhostCountText = $"Ghosts: {_ghostQuery.CalculateEntityCount()}";

            // Server tick
            if (!_networkTimeQuery.IsEmpty)
            {
                var nt = _networkTimeQuery.GetSingleton<NetworkTime>();
                _viewModel.TickText = $"Tick: {nt.ServerTick.SerializedData}";
            }
            else
            {
                _viewModel.TickText = "Tick: -";
            }
        }

        void SendRpc<T>() where T : unmanaged, IRpcCommand
        {
            if (_clientWorld == null || !_clientWorld.IsCreated) return;
            var em = _clientWorld.EntityManager;
            var req = em.CreateEntity();
            em.AddComponentData(req, default(T));
            em.AddComponentData(req, new SendRpcCommandRequest()); // Entity.Null target = server
        }
    }
}
