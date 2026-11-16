using System;
using System.Collections.Generic;
using UnityEngine;

namespace CSE377.AI
{
    /// <summary>
    /// Savaş sisi (Fog-of-War) filtreli, hile korumalı oyun anlık durum snapshot'ı.
    /// Yapay zekâya yalnızca oyuncunun görebildiği meşru bilgiler aktarılır.
    /// </summary>
    [Serializable]
    public class AiSafeSnapshot
    {
        public string match_id = "match-01";
        public string team_id = "BLUE";
        public ResourceState resources = new ResourceState();
        public List<UnitGroup> army = new List<UnitGroup>();
        public List<EnemyGroup> visible_enemies = new List<EnemyGroup>();
        public List<string> unlocked_tech = new List<string>();
        public List<GameEventSnapshot> recent_events = new List<GameEventSnapshot>();

        [Serializable]
        public class ResourceState
        {
            public int wood;
            public int food;
            public int gold;

            public ResourceState(int wood = 650, int food = 400, int gold = 250)
            {
                this.wood = wood;
                this.food = food;
                this.gold = gold;
            }
        }

        [Serializable]
        public class UnitGroup
        {
            public string type;
            public int count;

            public UnitGroup(string type, int count)
            {
                this.type = type;
                this.count = count;
            }
        }

        [Serializable]
        public class EnemyGroup
        {
            public string type;
            public int count;
            public string zone;

            public EnemyGroup(string type, int count, string zone)
            {
                this.type = type;
                this.count = count;
                this.zone = zone;
            }
        }

        [Serializable]
        public class GameEventSnapshot
        {
            public string type;       // "UNDER_ATTACK", "UNIT_LOST", "TECH_COMPLETED"
            public string zone;       // "north_gate", "main_base"
            public int seconds_ago;

            public GameEventSnapshot(string type, string zone, int seconds_ago)
            {
                this.type = type;
                this.zone = zone;
                this.seconds_ago = seconds_ago;
            }
        }

        /// <summary>
        /// Test ve prototip aşaması için varsayılan güvenli bir oyun durumu üretir.
        /// </summary>
        public static AiSafeSnapshot CreateMockSnapshot()
        {
            var snapshot = new AiSafeSnapshot();
            snapshot.match_id = "demo-match-01";
            snapshot.resources = new ResourceState(wood: 450, food: 200, gold: 150);
            
            snapshot.army.Add(new UnitGroup("spearman", 4));
            snapshot.army.Add(new UnitGroup("archer", 2));

            // SADECE görüş alanındaki düşmanlar (Fog-of-War dışı)
            snapshot.visible_enemies.Add(new EnemyGroup("cavalry", 3, "north_gate"));

            snapshot.unlocked_tech.Add("Age_1");
            snapshot.recent_events.Add(new GameEventSnapshot("UNDER_ATTACK", "north_gate", 12));

            return snapshot;
        }

        public string ToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }
    }
}
