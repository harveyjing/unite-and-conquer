using System;
using System.Globalization;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Demo
{
    // MonoBehaviour bridge from the client ECS world to BattleHud.uxml.
    // Lazy-finds the client world by name (mirrors DemoHudController);
    // each Update, counts ghost soldiers per team and writes formatted
    // strings into the BattleHudViewModel. Winner detection: once a
    // team has been seen alive, dropping to zero triggers the banner.
    [RequireComponent(typeof(UIDocument))]
    public class BattleHudController : MonoBehaviour
    {
        BattleHudViewModel _viewModel;

        World _clientWorld;
        EntityQuery _soldierQuery;

        bool _redEverAlive;
        bool _blueEverAlive;
        int  _winnerTeam = -1;   // -1 = undecided, 0 = Red, 1 = Blue

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            var root = doc.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("BattleHudController: UIDocument rootVisualElement is null.");
                enabled = false;
                return;
            }

            _viewModel = new BattleHudViewModel();
            root.dataSource = _viewModel;
        }

        void OnDisable()
        {
            if (_soldierQuery != default) _soldierQuery.Dispose();
            _soldierQuery = default;
            _clientWorld = null;
        }

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
                if (_soldierQuery != default) _soldierQuery.Dispose();
                _soldierQuery = default;
                _clientWorld = FindClientWorld();
                if (_clientWorld == null) return;

                _soldierQuery = _clientWorld.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<Soldier>(),
                    ComponentType.ReadOnly<Team>());
            }

            int red = 0, blue = 0;
            using (var teams = _soldierQuery.ToComponentDataArray<Team>(Allocator.Temp))
            {
                for (int i = 0; i < teams.Length; i++)
                {
                    if (teams[i].Value == 0) red++;
                    else if (teams[i].Value == 1) blue++;
                }
            }

            if (red  > 0) _redEverAlive  = true;
            if (blue > 0) _blueEverAlive = true;

            _viewModel.RedAliveText  = string.Format(CultureInfo.InvariantCulture, "Red:  {0}", red);
            _viewModel.BlueAliveText = string.Format(CultureInfo.InvariantCulture, "Blue: {0}", blue);

            if (_winnerTeam == -1)
            {
                if (_redEverAlive  && red  == 0 && blue > 0) _winnerTeam = 1;
                if (_blueEverAlive && blue == 0 && red  > 0) _winnerTeam = 0;
                if (_redEverAlive  && _blueEverAlive && red == 0 && blue == 0) _winnerTeam = 2; // draw
            }

            _viewModel.WinnerText = _winnerTeam switch
            {
                0 => "RED WINS",
                1 => "BLUE WINS",
                2 => "DRAW",
                _ => string.Empty,
            };
        }
    }
}
