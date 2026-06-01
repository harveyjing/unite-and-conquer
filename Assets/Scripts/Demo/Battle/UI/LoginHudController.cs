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
    //   (a Soldier with GhostOwnerIsLocal exists) hide the panel.
    [RequireComponent(typeof(UIDocument))]
    public class LoginHudController : MonoBehaviour
    {
        VisualElement _panel;
        TextField _usernameField;
        Button _enterBtn;
        EventCallback<ClickEvent> _enterHandler;

        World _clientWorld;
        EntityQuery _ownedSoldierQuery;
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

            _panel         = root.Q<VisualElement>("login-panel");
            _usernameField = root.Q<TextField>("username-field");
            _enterBtn      = root.Q<Button>("enter-btn");

            _enterHandler = _ => SubmitLogin();
            _enterBtn?.RegisterCallback(_enterHandler);
        }

        void OnDisable()
        {
            if (_enterBtn != null && _enterHandler != null)
                _enterBtn.UnregisterCallback(_enterHandler);
            if (_ownedSoldierQuery != default) _ownedSoldierQuery.Dispose();
            _ownedSoldierQuery = default;
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
                if (_ownedSoldierQuery != default) _ownedSoldierQuery.Dispose();
                _ownedSoldierQuery = default;
                _clientWorld = FindClientWorld();
                if (_clientWorld == null) return;

                _ownedSoldierQuery = _clientWorld.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<Soldier>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>());
            }

            if (_loggedIn) return;

            if (!_ownedSoldierQuery.IsEmpty)
            {
                _loggedIn = true;
                if (_panel != null) _panel.style.display = DisplayStyle.None;
            }
        }
    }
}
