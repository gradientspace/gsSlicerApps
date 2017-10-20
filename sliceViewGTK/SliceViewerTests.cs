using System;
using System.Collections.Generic;
using System.Linq;
using Gtk;
using GLib;
using g3;
using gs;

namespace SliceViewer
{
	public static class SliceViewerTests
	{


        public static void TestDGraph2()
		{
			Window window = new Window("TestDGraph2");
			window.SetDefaultSize(600, 600);
			window.SetPosition(WindowPosition.Center);

            DebugViewCanvas view = new DebugViewCanvas();

            GeneralPolygon2d poly = new GeneralPolygon2d(
				Polygon2d.MakeCircle(10, 32));

            Polygon2d hole = Polygon2d.MakeCircle(5, 32);
            hole.Translate(new Vector2d(2, 0));
            hole.Reverse();
            poly.AddHole(hole);

            Polygon2d hole2 = Polygon2d.MakeCircle(1, 32);
            hole2.Translate(-6 * Vector2d.AxisX);
            hole2.Reverse();
            poly.AddHole(hole2);

            Polygon2d hole3 = Polygon2d.MakeCircle(1, 32);
            hole3.Translate(-6 * Vector2d.One);
            hole3.Reverse();
            poly.AddHole(hole3);

            Polygon2d hole4 = Polygon2d.MakeCircle(1, 32);
            hole4.Translate(7 * Vector2d.AxisY);
            hole4.Reverse();
            poly.AddHole(hole4);

            view.AddPolygon(poly, Colorf.Black);

            double spacing = 0.2;

            //double[] offsets = new double[] { 0.5, 1, 1.5, 2, 2.5 };
            double[] offsets = new double[] { 4 };

            foreach (double offset in offsets) {
                DGraph2 graph = Offset(poly, offset, spacing, false);
                DGraph2Util.Curves c = DGraph2Util.ExtractCurves(graph);
                view.AddGraph(graph, Colorf.Red);
            }



            window.Add(view);
			window.ShowAll();
		}




		static DVector<Vector2d> offset_cache;
        static DVector<Vector2d> position_cache;
        static DVector<Vector2d> collapse_cache;
        static GeneralPolygon2dBoxTree poly_tree;
        static PointHashGrid2d<int> graph_cache;

        public static DGraph2 Offset(GeneralPolygon2d poly, double fOffset, double fTargetSpacing, bool bResolveOverlaps)
        {
            double dt = fTargetSpacing / 2;
            int nSteps = (int)( Math.Abs(fOffset) / dt );
            nSteps += nSteps / 3;

			DGraph2 graph = new DGraph2();
			graph.AppendPolygon(poly.Outer);
			foreach (var h in poly.Holes)
				graph.AppendPolygon(h);

            graph_cache = null;
			SplitToMaxEdgeLength(graph, fTargetSpacing * 1.5);

			offset_cache = new DVector<Vector2d>();
			offset_cache.resize(graph.VertexCount*2);
            position_cache = new DVector<Vector2d>();
            position_cache.resize(graph.VertexCount * 2);
            poly_tree = new GeneralPolygon2dBoxTree(poly);
            collapse_cache = new DVector<Vector2d>();
            collapse_cache.resize(graph.VertexCount * 2);

            graph_cache = new PointHashGrid2d<int>(poly.Bounds.MaxDim / 64, -1);
            foreach (int vid in graph.VertexIndices())
                graph_cache.InsertPoint(vid, graph.GetVertex(vid));

            LocalProfiler p = new LocalProfiler();


			for (int i = 0; i < nSteps; ++i ) {

				p.Start("offset");

                /*
                 * ISSUE: in very thin regions, we will still try to take a step at least as large as dt,
                 * and then a second step 'back'. It would be better if we could keep track of how large
                 * a step we managed to take at each vertex, and scale dt to be in that range.
                 * 
                 */

                gParallel.ForEach(graph.VertexIndices(), (vid) => {
                    Vector2d cur_pos = graph.GetVertex(vid);
                    double err, err_2;
                    // take two sequential steps and average them. this vastly improves convergence.
                    Vector2d new_pos = compute_offset_step(cur_pos, poly, fOffset, dt, out err);
                    Vector2d new_pos_2 = compute_offset_step(new_pos, poly, fOffset, dt, out err_2);
                    if (err < MathUtil.ZeroTolerancef)
                        err = MathUtil.ZeroTolerancef;
                    if (err_2 < MathUtil.ZeroTolerancef)
                        err_2 = MathUtil.ZeroTolerancef;

                    double w = 1.0 / err, w_2 = 1.0 / err_2;
                    new_pos = w * new_pos + w_2 * new_pos_2;
                    new_pos /= (w + w_2);

                    //new_pos = Vector2d.Lerp(new_pos, new_pos_2, 0.5);

                    graph_cache.UpdatePoint(vid, cur_pos, new_pos);
                    graph.SetVertex(vid, new_pos);
                });

				p.StopAndAccumulate("offset");
				p.Start("smooth");

                SmoothPass(graph, 5, 0.75, fTargetSpacing / 2);

				p.StopAndAccumulate("smooth");
				p.Start("join");

				int joined = 0;
				do {
                    //joined = JoinInTolerance(graph, fMergeThresh);
                    //joined = JoinInTolerance_Parallel(graph, fMergeThresh);
                    joined = JoinInTolerance_Parallel_Cache(graph, fTargetSpacing);
                } while (joined > 0);

				p.StopAndAccumulate("join");
				p.Start("refine");

				CollapseToMinEdgeLength(graph, fTargetSpacing * 0.75f);
				SplitToMaxEdgeLength(graph, fTargetSpacing * 1.5);

				p.StopAndAccumulate("refine");
			}

			System.Console.WriteLine(p.AllAccumulatedTimes());

            //SmoothPass(graph, 25, 0.1, fMergeThresh);

            if (bResolveOverlaps) {
                List<int> junctions = new List<int>();
                foreach (int vid in graph.VertexIndices()) {
                    if (graph.GetVtxEdgeCount(vid) > 2)
                        junctions.Add(vid);
                }
                foreach (int vid in junctions) {
                    Vector2d v = graph.GetVertex(vid);
                    int[] nbr_verts = graph.VtxVerticesItr(vid).ToArray();
                    Index2i best_aligned = Index2i.Max; double max_angle = 0;
                    for (int i = 0; i < nbr_verts.Length; ++i) {
                        for (int j = i + 1; j < nbr_verts.Length; ++j) {
                            double angle = Vector2d.AngleD(
                                (graph.GetVertex(nbr_verts[i]) - v).Normalized,
                                (graph.GetVertex(nbr_verts[j]) - v).Normalized);
                            angle = Math.Abs(angle);
                            if (angle > max_angle) {
                                max_angle = angle;
                                best_aligned = new Index2i(nbr_verts[i], nbr_verts[j]);
                            }
                        }
                    }

                    for (int k = 0; k < nbr_verts.Length; ++k) {
                        if (nbr_verts[k] == best_aligned.a || nbr_verts[k] == best_aligned.b)
                            continue;
                        int eid = graph.FindEdge(vid, nbr_verts[k]);
                        graph.RemoveEdge(eid, true);
                        if (graph.IsVertex(nbr_verts[k])) {
                            Vector2d newpos = Vector2d.Lerp(graph.GetVertex(nbr_verts[k]), v, 0.99);
                            int newv = graph.AppendVertex(newpos);
                            graph.AppendEdge(nbr_verts[k], newv);
                        }
                    }
                }

                PathOverlapRepair repair = new PathOverlapRepair(graph);
                repair.Compute();
            }


            return graph;
		}





		public static Vector2d compute_offset_step(Vector2d cur_pos, GeneralPolygon2d poly, double fTargetOffset, double stepSize, out double err) {

			int iHole, iSeg; double segT;
            double distSqr =
                //poly.DistanceSquared(cur_pos, out iHole, out iSeg, out segT);
                poly_tree.DistanceSquared(cur_pos, out iHole, out iSeg, out segT);

            double dist = Math.Sqrt(distSqr);
			Vector2d normal = poly.GetNormal(iSeg, segT, iHole);

            if ( fTargetOffset < 0 ) {
                fTargetOffset = -fTargetOffset;
                normal = -normal;
            }

			double step = stepSize;
			if (dist > fTargetOffset) {
				step = Math.Max(fTargetOffset - dist, -step);
			} else {
				step = Math.Min(fTargetOffset - dist, step);
			}
            err = Math.Abs(fTargetOffset - dist);

			Vector2d new_pos = cur_pos - step * normal;
			return new_pos;
		}







		public static void SmoothPass(DGraph2 graph, int passes, double smooth_alpha, double max_move)
		{
			double max_move_sqr = max_move * max_move;
			int NV = graph.MaxVertexID;
			DVector<Vector2d> smoothedV = offset_cache;
			if (smoothedV.size < NV)
				smoothedV.resize(NV);

            if (position_cache.size < NV)
                position_cache.resize(NV);

            for (int pi = 0; pi < passes; ++pi) {

                gParallel.ForEach(Interval1i.Range(NV), (vid) => {
                    if (!graph.IsVertex(vid))
                        return;
                    Vector2d v = graph.GetVertex(vid);
                    Vector2d c = Vector2d.Zero;
                    int n = 0;
                    foreach (int vnbr in graph.VtxVerticesItr(vid)) {
                        c += graph.GetVertex(vnbr);
                        n++;
                    }
                    if (n >= 2) {
                        c /= n;
                        Vector2d dv = (smooth_alpha) * (c - v);
                        if (dv.LengthSquared > max_move_sqr) {
                            double d = dv.Normalize();
                            dv *= max_move;
                        }
                        v += dv;
                    }
                    smoothedV[vid] = v;
                });

                if (pi == 0) {
                    for (int vid = 0; vid < NV; ++vid) {
                        if (graph.IsVertex(vid)) {
                            position_cache[vid] = graph.GetVertex(vid);
                            graph.SetVertex(vid, smoothedV[vid]);
                        }
                    }
                } else {
                    for (int vid = 0; vid < NV; ++vid) {
                        if (graph.IsVertex(vid)) 
                            graph.SetVertex(vid, smoothedV[vid]);
                    }
                }
			}

            for (int vid = 0; vid < NV; ++vid) {
                if (graph.IsVertex(vid)) 
                    graph_cache.UpdatePointUnsafe(vid, position_cache[vid], smoothedV[vid]);
            }
            

        }




        public static int JoinInTolerance_Parallel(DGraph2 graph, double fMergeDist)
        {
            double mergeSqr = fMergeDist * fMergeDist;

            int NV = graph.MaxVertexID;
            if ( collapse_cache.size < NV )
                collapse_cache.resize(NV);

            gParallel.ForEach( Interval1i.Range(NV), (a) => {
                collapse_cache[a] = new Vector2d(-1, double.MaxValue);
                if (!graph.IsVertex(a))
                    return;

                Vector2d va = graph.GetVertex(a);

                int bNearest = -1;
                double nearDistSqr = double.MaxValue;
                for (int b = a + 1; b < NV; ++b) {
                    if (b == a || graph.IsVertex(b) == false)
                        continue;
                    double distsqr = va.DistanceSquared(graph.GetVertex(b));
                    if (distsqr < mergeSqr && distsqr < nearDistSqr) {
                        if (graph.FindEdge(a, b) == DGraph2.InvalidID) {
                            nearDistSqr = distsqr;
                            bNearest = b;
                        }
                    }
                }
                if (bNearest != -1)
                    collapse_cache[a] = new Vector2d(bNearest, nearDistSqr);
            });

            // [TODO] sort

            int merged = 0;
            for (int a = 0; a < NV; ++a) {
                if (collapse_cache[a].x == -1)
                    continue;

                int bNearest = (int)collapse_cache[a].x;

                Vector2d pos_a = graph.GetVertex(a);
                Vector2d pos_bNearest = graph.GetVertex(bNearest);

                int eid = graph.AppendEdge(a, bNearest);
                DGraph2.EdgeCollapseInfo collapseInfo;
                graph.CollapseEdge(bNearest, a, out collapseInfo);
                graph_cache.RemovePointUnsafe(a, pos_a);
                graph_cache.UpdatePointUnsafe(bNearest, pos_bNearest, graph.GetVertex(bNearest));
                merged++;
            }
            return merged;
        }





        public static int JoinInTolerance_Parallel_Cache(DGraph2 graph, double fMergeDist)
        {
            double mergeSqr = fMergeDist * fMergeDist;

            int NV = graph.MaxVertexID;
            if (collapse_cache.size < NV)
                collapse_cache.resize(NV);

            gParallel.ForEach_Sequential(Interval1i.Range(NV), (a) => {
                collapse_cache[a] = new Vector2d(-1, double.MaxValue);
                if (!graph.IsVertex(a))
                    return;

                Vector2d va = graph.GetVertex(a);

                KeyValuePair<int, double> found =
                    graph_cache.FindNearestInRadius(va, mergeSqr,
                        (b) => { return va.DistanceSquared(graph.GetVertex(b)); },
                        (b) => { return b <= a || (graph.FindEdge(a, b) != DGraph2.InvalidID); });

                if (found.Key != -1) {
                    collapse_cache[a] = new Vector2d(found.Key, found.Value);
                }
            });

            // [TODO] sort

            int merged = 0;
            for (int a = 0; a < NV; ++a) {
                if (collapse_cache[a].x == -1)
                    continue;

                int bNearest = (int)collapse_cache[a].x;
                if (!graph.IsVertex(bNearest))
                    continue;

                Vector2d pos_a = graph.GetVertex(a);
                Vector2d pos_bNearest = graph.GetVertex(bNearest);

                int eid = graph.AppendEdge(a, bNearest);
                DGraph2.EdgeCollapseInfo collapseInfo;
                graph.CollapseEdge(bNearest, a, out collapseInfo);

                graph_cache.RemovePointUnsafe(a, pos_a);
                graph_cache.UpdatePointUnsafe(bNearest, pos_bNearest, graph.GetVertex(bNearest));
                collapse_cache[bNearest] = new Vector2d(-1, double.MaxValue);

                merged++;
            }
            return merged;
        }




        public static void CollapseToMinEdgeLength(DGraph2 graph, double fMinLen) {
            double sharp_threshold_deg = 140.0f;

			double minLenSqr = fMinLen * fMinLen;
			bool done = false;
            int max_passes = 100;
            int pass_count = 0;
			while (done == false && pass_count++ < max_passes) {
				done = true;

                // [RMS] do modulo-indexing here to avoid pathological cases where we do things like
                // continually collapse a short edge adjacent to a long edge (which will result in crazy over-collapse)
                int N = graph.MaxEdgeID;
                const int nPrime = 31337;     // any prime will do...
                int cur_eid = 0;
                do {
                    int eid = cur_eid;
                    cur_eid = (cur_eid + nPrime) % N;

                    if (!graph.IsEdge(eid))
                        continue;
                    Index2i ev = graph.GetEdgeV(eid);

                    Vector2d va = graph.GetVertex(ev.a);
                    Vector2d vb = graph.GetVertex(ev.b);
                    double distSqr = va.DistanceSquared(vb);
                    if (distSqr < minLenSqr) {

                        int vtx_idx = -1;    // collapse to this vertex

                        // check valences. want to preserve positions of non-valence-2
                        int na = graph.GetVtxEdgeCount(ev.a);
                        int nb = graph.GetVtxEdgeCount(ev.b);
                        if (na != 2 && nb != 2)
                            continue;
                        if (na != 2)
                            vtx_idx = 0;
                        else if (nb != 2)
                            vtx_idx = 1;

                        // check opening angles. want to preserve sharp(er) angles
                        if (vtx_idx == -1) {
                            double opena = Math.Abs(graph.OpeningAngle(ev.a));
                            double openb = Math.Abs(graph.OpeningAngle(ev.b));
                            if (opena < sharp_threshold_deg && openb < sharp_threshold_deg)
                                continue;
                            else if (opena < sharp_threshold_deg)
                                vtx_idx = 0;
                            else if (openb < sharp_threshold_deg)
                                vtx_idx = 1;
                        }

                        Vector2d newPos = (vtx_idx == -1) ? 0.5 * (va + vb) : ((vtx_idx == 0) ? va : vb);

                        int keep = ev.a, remove = ev.b;
                        if (vtx_idx == 1) {
                            remove = ev.a; keep = ev.b;
                        }

                        Vector2d remove_pos = graph.GetVertex(remove);
                        Vector2d keep_pos = graph.GetVertex(keep);

                        DGraph2.EdgeCollapseInfo collapseInfo;
                        if (graph.CollapseEdge(keep, remove, out collapseInfo) == MeshResult.Ok) {
                            graph_cache.RemovePointUnsafe(collapseInfo.vRemoved, remove_pos);
                            graph_cache.UpdatePointUnsafe(collapseInfo.vKept, keep_pos, newPos);
                            graph.SetVertex(collapseInfo.vKept, newPos);
                            done = false;
                        }
                    }

                } while (cur_eid != 0);
			}
		}









        









        public static void SplitToMaxEdgeLength(DGraph2 graph, double fMaxLen) {
			List<int> queue = new List<int>();
			int NE = graph.MaxEdgeID;
			for (int eid = 0; eid < NE; ++eid) {
				if (!graph.IsEdge(eid))
					continue;
				Index2i ev = graph.GetEdgeV(eid);
				double dist = graph.GetVertex(ev.a).Distance(graph.GetVertex(ev.b));
				if (dist > fMaxLen) {
					DGraph2.EdgeSplitInfo splitInfo;
					if (graph.SplitEdge(eid, out splitInfo) == MeshResult.Ok ) {
                        if (graph_cache != null)
                            graph_cache.InsertPointUnsafe(splitInfo.vNew, graph.GetVertex(splitInfo.vNew));
                        if (dist > 2 * fMaxLen) {
                            queue.Add(eid);
                            queue.Add(splitInfo.eNewBN);
                        }
					}
				}
			}
			while (queue.Count > 0) {
				int eid = queue[queue.Count - 1];
				queue.RemoveAt(queue.Count - 1);
				if (!graph.IsEdge(eid))
					continue;
				Index2i ev = graph.GetEdgeV(eid);
				double dist = graph.GetVertex(ev.a).Distance(graph.GetVertex(ev.b));
				if (dist > fMaxLen) {
					DGraph2.EdgeSplitInfo splitInfo;
					if (graph.SplitEdge(eid, out splitInfo) == MeshResult.Ok ) {
                        if (graph_cache != null)
                            graph_cache.InsertPointUnsafe(splitInfo.vNew, graph.GetVertex(splitInfo.vNew));
                        if (dist > 2 * fMaxLen) {
                            queue.Add(eid);
                            queue.Add(splitInfo.eNewBN);
                        }
					}
				}				
			}
		}











 



	}
}
