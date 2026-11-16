using System.Collections.Generic;
using UnityEngine;

namespace CSE377.Gameplay
{
    /// <summary>
    /// Binaların, surların ve nehrin etrafından akıcı ve kesin şekilde dolanan
    /// hafif 2D Grid A* + Line-of-Sight (String Pulling) yol bulucu.
    /// </summary>
    public static class RTSPathfinder
    {
        private const float WorldHalfSize = 70f;
        private const float CellSize = 0.70f;
        private const int GridDim = 200; // 140m / 0.70m = 200x200
        private const int MaxCells = GridDim * GridDim;

        private static readonly bool[,] blocked = new bool[GridDim, GridDim];
        private static readonly int[] gCost = new int[MaxCells];
        private static readonly int[] fCost = new int[MaxCells];
        private static readonly int[] parentIdx = new int[MaxCells];
        private static readonly byte[] state = new byte[MaxCells]; // 0=unvisited, 1=open, 2=closed
        private static readonly int[] openHeap = new int[MaxCells];
        private static readonly int[] heapIndex = new int[MaxCells];
        private static int openCount = 0;

        private static readonly int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly int[] dCost = { 10, 10, 10, 10, 14, 14, 14, 14 };

        private static void HeapSwap(int a, int b)
        {
            int nodeA = openHeap[a];
            int nodeB = openHeap[b];
            openHeap[a] = nodeB;
            openHeap[b] = nodeA;
            heapIndex[nodeB] = a;
            heapIndex[nodeA] = b;
        }

        private static void HeapSiftUp(int pos)
        {
            while (pos > 0)
            {
                int parent = (pos - 1) >> 1;
                if (fCost[openHeap[pos]] < fCost[openHeap[parent]])
                {
                    HeapSwap(pos, parent);
                    pos = parent;
                }
                else
                {
                    break;
                }
            }
        }

        private static void HeapSiftDown(int pos)
        {
            while (true)
            {
                int left = (pos << 1) + 1;
                if (left >= openCount) break;
                int right = left + 1;
                int smallest = (right < openCount && fCost[openHeap[right]] < fCost[openHeap[left]]) ? right : left;
                if (fCost[openHeap[smallest]] < fCost[openHeap[pos]])
                {
                    HeapSwap(pos, smallest);
                    pos = smallest;
                }
                else
                {
                    break;
                }
            }
        }

        private static void HeapPush(int nodeIdx)
        {
            openHeap[openCount] = nodeIdx;
            heapIndex[nodeIdx] = openCount;
            openCount++;
            HeapSiftUp(openCount - 1);
        }

        private static int HeapPop()
        {
            int top = openHeap[0];
            openCount--;
            if (openCount > 0)
            {
                int last = openHeap[openCount];
                openHeap[0] = last;
                heapIndex[last] = 0;
                HeapSiftDown(0);
            }
            heapIndex[top] = -1;
            return top;
        }

        private static int WorldToCell(float val)
        {
            return Mathf.Clamp(Mathf.FloorToInt((val + WorldHalfSize) / CellSize), 0, GridDim - 1);
        }

        private static Vector3 CellToWorld(int gx, int gz, float y)
        {
            float wx = -WorldHalfSize + (gx + 0.5f) * CellSize;
            float wz = -WorldHalfSize + (gz + 0.5f) * CellSize;
            return new Vector3(wx, y, wz);
        }

        private static void RebuildGrid(UnitTeam unitTeam, RTSBuilding ignoreBuilding)
        {
            System.Array.Clear(blocked, 0, blocked.Length);

            // 1. Nehir engeli (Köprüler ve sığlık hariç)
            if (RTSAutoBootstrap.HasRiver)
            {
                for (int gz = 0; gz < GridDim; gz++)
                {
                    float wz = -WorldHalfSize + (gz + 0.5f) * CellSize;
                    if (Mathf.Abs(wz) >= 5.5f) continue;

                    for (int gx = 0; gx < GridDim; gx++)
                    {
                        float wx = -WorldHalfSize + (gx + 0.5f) * CellSize;
                        bool onWest = Mathf.Abs(wx - (-28f)) <= 3.6f;
                        bool onEast = Mathf.Abs(wx - 28f) <= 3.6f;
                        bool onShallow = Mathf.Abs(wx) <= 3.2f;
                        if (!onWest && !onEast && !onShallow)
                        {
                            blocked[gx, gz] = true;
                        }
                    }
                }
            }

            // 2. Binalar (Birim yarıçapına uygun hücre merkezi mesafesi kontrolü — dar geçitleri korur)
            float unitClearance = 0.40f;
            float clearanceSq = unitClearance * unitClearance;
            foreach (var b in RTSBuilding.AllBuildings)
            {
                if (b == null || !b.gameObject.activeInHierarchy || b == ignoreBuilding || b.currentHp <= 0) continue;
                // Dost Şehir Kapısı açık geçittir
                if (b.buildingType == RTSBuildingType.CityGate && b.team == unitTeam) continue;

                Collider bCol = b.GetComponent<Collider>();
                Vector3 center = (bCol != null) ? bCol.bounds.center : b.transform.position;
                Vector3 ext = (bCol != null) ? bCol.bounds.extents : b.GetBuildingFootprint() * 0.5f;

                int minX = WorldToCell(center.x - ext.x - unitClearance);
                int maxX = WorldToCell(center.x + ext.x + unitClearance);
                int minZ = WorldToCell(center.z - ext.z - unitClearance);
                int maxZ = WorldToCell(center.z + ext.z + unitClearance);

                bool useCollider = (bCol != null && bCol.enabled);
                float queryY = center.y;

                for (int gx = minX; gx <= maxX; gx++)
                {
                    float wx = -WorldHalfSize + (gx + 0.5f) * CellSize;
                    for (int gz = minZ; gz <= maxZ; gz++)
                    {
                        if (blocked[gx, gz]) continue;
                        float wz = -WorldHalfSize + (gz + 0.5f) * CellSize;

                        if (useCollider)
                        {
                            Vector3 closest = bCol.ClosestPoint(new Vector3(wx, queryY, wz));
                            float offX = wx - closest.x;
                            float offZ = wz - closest.z;
                            if (offX * offX + offZ * offZ < clearanceSq)
                            {
                                blocked[gx, gz] = true;
                            }
                        }
                        else
                        {
                            float offX = Mathf.Max(0f, Mathf.Abs(wx - center.x) - ext.x);
                            float offZ = Mathf.Max(0f, Mathf.Abs(wz - center.z) - ext.z);
                            if (offX * offX + offZ * offZ < clearanceSq)
                            {
                                blocked[gx, gz] = true;
                            }
                        }
                    }
                }
            }

            // 3. Dost Şehir Kapısı Kemer Geçidini Açık Tut (Komşu surların padding'i geçidi kapatmasın)
            foreach (var b in RTSBuilding.AllBuildings)
            {
                if (b == null || !b.gameObject.activeInHierarchy || b.currentHp <= 0) continue;
                if (b.buildingType != RTSBuildingType.CityGate || b.team != unitTeam) continue;

                int cGx = WorldToCell(b.transform.position.x);
                int cGz = WorldToCell(b.transform.position.z);
                for (int gx = Mathf.Max(0, cGx - 4); gx <= Mathf.Min(GridDim - 1, cGx + 4); gx++)
                {
                    for (int gz = Mathf.Max(0, cGz - 4); gz <= Mathf.Min(GridDim - 1, cGz + 4); gz++)
                    {
                        Vector3 cellWorld = CellToWorld(gx, gz, b.transform.position.y);
                        Vector3 localCell = b.transform.InverseTransformPoint(cellWorld);
                        if (Mathf.Abs(localCell.x) <= 1.35f && Mathf.Abs(localCell.z) <= 2.4f)
                        {
                            blocked[gx, gz] = false;
                        }
                    }
                }
            }
        }

        private static bool FindNearestWalkable(int cx, int cz, out int outX, out int outZ)
        {
            if (!blocked[cx, cz])
            {
                outX = cx;
                outZ = cz;
                return true;
            }

            for (int r = 1; r <= 16; r++)
            {
                float bestDist = float.MaxValue;
                int bestX = -1;
                int bestZ = -1;
                for (int ox = -r; ox <= r; ox++)
                {
                    for (int oz = -r; oz <= r; oz++)
                    {
                        if (Mathf.Abs(ox) != r && Mathf.Abs(oz) != r) continue;
                        int nx = cx + ox;
                        int nz = cz + oz;
                        if (nx < 0 || nx >= GridDim || nz < 0 || nz >= GridDim) continue;
                        if (!blocked[nx, nz])
                        {
                            float d = ox * ox + oz * oz;
                            if (d < bestDist)
                            {
                                bestDist = d;
                                bestX = nx;
                                bestZ = nz;
                            }
                        }
                    }
                }
                if (bestX >= 0)
                {
                    outX = bestX;
                    outZ = bestZ;
                    return true;
                }
            }

            outX = cx;
            outZ = cz;
            return false;
        }

        private static bool HasLineOfSight(int x0, int z0, int x1, int z1)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(z1 - z0)) * 4;
            if (steps <= 1) return true;

            int prevX = x0;
            int prevZ = z0;

            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                int gx = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
                int gz = Mathf.RoundToInt(Mathf.Lerp(z0, z1, t));
                if (gx < 0 || gx >= GridDim || gz < 0 || gz >= GridDim || blocked[gx, gz])
                {
                    return false;
                }

                // Çapraz geçişlerde bina köşesinden kesmeyi (corner-clipping) önle
                if (gx != prevX && gz != prevZ)
                {
                    if (blocked[gx, prevZ] || blocked[prevX, gz])
                    {
                        return false;
                    }
                }

                prevX = gx;
                prevZ = gz;
            }
            return true;
        }

        public static List<Vector3> FindPath(Vector3 start, Vector3 target, UnitTeam unitTeam, RTSBuilding ignoreBuilding = null)
        {
            var result = new List<Vector3>();
            RebuildGrid(unitTeam, ignoreBuilding);

            int sx = WorldToCell(start.x);
            int sz = WorldToCell(start.z);
            int tx = WorldToCell(target.x);
            int tz = WorldToCell(target.z);

            FindNearestWalkable(sx, sz, out sx, out sz);
            FindNearestWalkable(tx, tz, out tx, out tz);

            // Eğer başlangıç ile hedef arasında doğrudan engelsiz görüş hattı varsa doğrudan hedefe git
            if (HasLineOfSight(sx, sz, tx, tz))
            {
                result.Add(target);
                return result;
            }

            System.Array.Clear(state, 0, state.Length);
            openCount = 0;

            int startIdx = sz * GridDim + sx;
            int targetIdx = tz * GridDim + tx;

            gCost[startIdx] = 0;
            fCost[startIdx] = Heuristic(sx, sz, tx, tz);
            parentIdx[startIdx] = -1;
            state[startIdx] = 1;
            HeapPush(startIdx);

            bool found = false;
            int iterations = 0;
            const int maxIterations = 18000;

            while (openCount > 0 && iterations < maxIterations)
            {
                iterations++;
                int current = HeapPop();
                state[current] = 2;

                if (current == targetIdx)
                {
                    found = true;
                    break;
                }

                int cx = current % GridDim;
                int cz = current / GridDim;

                for (int dir = 0; dir < 8; dir++)
                {
                    int nx = cx + dx[dir];
                    int nz = cz + dz[dir];
                    if (nx < 0 || nx >= GridDim || nz < 0 || nz >= GridDim) continue;
                    if (blocked[nx, nz]) continue;

                    // Çapraz geçişte köşedeki binaya sürtünmeyi önle
                    if (dir >= 4 && (blocked[cx + dx[dir], cz] || blocked[cx, cz + dz[dir]]))
                    {
                        continue;
                    }

                    int nIdx = nz * GridDim + nx;
                    if (state[nIdx] == 2) continue;

                    int newG = gCost[current] + dCost[dir];
                    if (state[nIdx] != 1 || newG < gCost[nIdx])
                    {
                        gCost[nIdx] = newG;
                        fCost[nIdx] = newG + Heuristic(nx, nz, tx, tz);
                        parentIdx[nIdx] = current;
                        if (state[nIdx] != 1)
                        {
                            state[nIdx] = 1;
                            HeapPush(nIdx);
                        }
                        else
                        {
                            HeapSiftUp(heapIndex[nIdx]);
                        }
                    }
                }
            }

            if (!found)
            {
                result.Add(target);
                return result;
            }

            // Yolu geriye doğru çıkar
            var rawCells = new List<int>();
            int trace = targetIdx;
            while (trace != -1)
            {
                rawCells.Add(trace);
                trace = parentIdx[trace];
            }
            rawCells.Reverse();

            // Line-of-Sight ile gereksiz ara noktaları sadeleştir (Smooth Path)
            int anchor = 0;
            for (int i = 1; i < rawCells.Count; i++)
            {
                int ax = rawCells[anchor] % GridDim;
                int az = rawCells[anchor] / GridDim;
                int ix = rawCells[i] % GridDim;
                int iz = rawCells[i] / GridDim;

                if (!HasLineOfSight(ax, az, ix, iz))
                {
                    int prevCell = rawCells[i - 1];
                    int px = prevCell % GridDim;
                    int pz = prevCell / GridDim;
                    result.Add(CellToWorld(px, pz, start.y));
                    anchor = i - 1;
                }
            }

            result.Add(target);
            return result;
        }

        private static int Heuristic(int x0, int z0, int x1, int z1)
        {
            int adx = Mathf.Abs(x1 - x0);
            int adz = Mathf.Abs(z1 - z0);
            int min = Mathf.Min(adx, adz);
            int max = Mathf.Max(adx, adz);
            return 14 * min + 10 * (max - min);
        }
    }
}
