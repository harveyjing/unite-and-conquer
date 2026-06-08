using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Demo
{
    // Bridges the login UIDocument to the ECS client world.
    //
    // OnEnable: wire the username field + Enter button.
    // Enter click: write a PendingAuth entity into the client world
    //   (ClientAuthSendSystem turns it into an AuthenticateRequest RPC).
    // Update: lazy-find the client world; once the local player owns a soldier
    //   (a Soldier whose replicated GhostOwner.NetworkId matches this client's
    //   NetworkId) hide the overlay. Ownership is detected by NetworkId comparison
    //   rather than the GhostOwnerIsLocal tag, which Netcode never adds to these plain
    //   interpolated soldier ghosts.
    [RequireComponent(typeof(UIDocument))]
    public class LoginHudController : MonoBehaviour
    {
        VisualElement _overlay;
        TextField _usernameField;
        Button _enterBtn;
        EventCallback<ClickEvent> _enterHandler;

        World _clientWorld;
        EntityQuery _networkIdQuery;
        EntityQuery _ownerQuery;
        bool _loggedIn;

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            var root = doc.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("LoginHudController: UIDocument rootVisualElement is null.");
                enabled = false;
                return;
            }

            _overlay       = root.Q<VisualElement>("login-overlay");
            _usernameField = root.Q<TextField>("username-field");
            _enterBtn      = root.Q<Button>("enter-btn");

            _enterHandler = _ => SubmitLogin();
            _enterBtn?.RegisterCallback(_enterHandler);
        }

        void OnDisable()
        {
            if (_enterBtn != null && _enterHandler != null)
                _enterBtn.UnregisterCallback(_enterHandler);
            if (_networkIdQuery != default) _networkIdQuery.Dispose();
            if (_ownerQuery != default) _ownerQuery.Dispose();
            _networkIdQuery = default;
            _ownerQuery = default;
            _clientWorld = null;
        }

        static World FindClientWorld()
        {
            foreach (var w in World.All)
                if (w.Name.IndexOf("client", StringComparison.OrdinalIgnoreCase) >= 0)
                    return w;
            return null;
        }

        void SubmitLogin()
        {
            var name = _usernameField?.value;
            if (string.IsNullOrWhiteSpace(name)) return; // server also rejects empty
            if (_clientWorld == null || !_clientWorld.IsCreated) return;

            var em = _clientWorld.EntityManager;
            var e = em.CreateEntity(typeof(PendingAuth));
            em.SetComponentData(e, new PendingAuth { Username = new FixedString64Bytes(name) });
        }

        void Update()
        {
            if (_clientWorld == null || !_clientWorld.IsCreated)
            {
                if (_networkIdQuery != default) _networkIdQuery.Dispose();
                if (_ownerQuery != default) _ownerQuery.Dispose();
                _networkIdQuery = default;
                _ownerQuery = default;
                _clientWorld = FindClientWorld();
                if (_clientWorld == null) return;

                var em = _clientWorld.EntityManager;
                _networkIdQuery = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
                _ownerQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<Soldier>(),
                    ComponentType.ReadOnly<GhostOwner>());
            }

            if (_loggedIn) return;

            // Not connected yet → no local NetworkId to compare against.
            if (_networkIdQuery.IsEmpty) return;
            int localId = _networkIdQuery.GetSingleton<NetworkId>().Value;
            if (localId == 0) return;

            using var owners = _ownerQuery.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            for (int i = 0; i < owners.Length; i++)
            {
                if (owners[i].NetworkId != localId) continue;
                _loggedIn = true;
                if (_overlay != null) _overlay.style.display = DisplayStyle.None;
                break;
            }
        }
    }
}
