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
        public WorkerAllocationState workers = new WorkerAllocationState();
        public PopulationState population = new PopulationState();
        public FogIntelState fog_intel = new FogIntelState();
        public List<UnitGroup> army = new List<UnitGroup>();
        public List<UnitGroup> friendly_buildings = new List<UnitGroup>();
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
        public class WorkerAllocationState
        {
            public int total;
            public int food;
            public int wood;
            public int gold;
            public int building;
            public int idle;
        }

        [Serializable]
        public class PopulationState
        {
            public int current = 4;
            public int max = 10;
        }

        [Serializable]
        public class EnemyMemoryGroup
        {
            public string type;
            public int count;
            public string zone;
            public int seconds_ago;
            public bool currently_visible;

            public EnemyMemoryGroup(string type, int count, string zone, int secondsAgo, bool currentlyVisible)
            {
                this.type = type;
                this.count = count;
                this.zone = zone;
                this.seconds_ago = secondsAgo;
                this.currently_visible = currentlyVisible;
            }
        }

        [Serializable]
        public class EnemyBuildingIntel
        {
            public string type;
            public int count;
            public string zone;
            public bool currently_visible;

            public EnemyBuildingIntel(string type, int count, string zone, bool currentlyVisible)
            {
                this.type = type;
                this.count = count;
                this.zone = zone;
                this.currently_visible = currentlyVisible;
            }
        }

        [Serializable]
        public class FogIntelState
        {
            public int explored_percent;
            public int visible_percent;
            public int discovered_wood_nodes;
            public int discovered_food_nodes;
            public int discovered_gold_nodes;
            public int seconds_until_next_raid = -1;
            public List<EnemyMemoryGroup> last_known_enemies = new List<EnemyMemoryGroup>();
            public List<EnemyBuildingIntel> discovered_enemy_buildings = new List<EnemyBuildingIntel>();
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
