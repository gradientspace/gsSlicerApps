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

        public static void TestOffset()
        {
            DMesh3 mesh = StandardMeshReader.ReadMesh("../../../sample_files/thinwall2.obj");
            MeshUtil.ScaleMesh(mesh, Frame3f.Identity, 1.1f * Vector3f.One);
            PrintMeshAssembly meshes = new PrintMeshAssembly();
            meshes.Meshes.Add(mesh);
            MeshPlanarSlicer slicer = new MeshPlanarSlicer();
            slicer.AddMeshes(meshes.Meshes);
            PlanarSliceStack slices = slicer.Compute();
            GeneralPolygon2d origPoly = slices[0].Solids[0];

            int k = 5;
            origPoly.Outer[k] = origPoly.Outer[k] + 4.6 * Vector2d.AxisY;

            List<GeneralPolygon2d> offsets = ClipperUtil.ComputeOffsetPolygon(origPoly, -0.2f, true);
            GeneralPolygon2d outer = offsets[0];
            offsets = ClipperUtil.ComputeOffsetPolygon(outer, -0.4f, true);
            GeneralPolygon2d offset = offsets[0];

            DGraph2 graph = new DGraph2();
            graph.AppendPolygon(offset.Outer);
            FilterSelfOverlaps(graph, 0.4f);
            //FilterSelfOverlaps(graph, 0.4f, false);

            DGraph2 outer_graph = new DGraph2();
            outer_graph.AppendPolygon(outer.Outer);
            FilterSelfOverlaps(outer_graph, 0.4f);


            Window window = new Window("TestDGraph2");
            window.SetDefaultSize(600, 600);
            window.SetPosition(WindowPosition.Center);

            DebugViewCanvas view = new DebugViewCanvas();
            view.AddPolygon(origPoly, Colorf.Orange);
            //view.AddPolygon(outer, Colorf.Red);
            view.AddGraph(outer_graph, Colorf.Red);
            view.AddGraph(graph, Colorf.Blue);
            window.Add(view);
            window.ShowAll();

        }





        static void FilterSelfOverlaps(DGraph2 graph, double overlapRadius, bool bResample = true)
        {
            // [RMS] this tolerance business is not workign properly right now. The problem is
            //  that decimator loses corners!

            // To simplify the computation we are going to resample the curve so that no adjacent
            // are within a given distance. Then we can use distance-to-segments, with the two adjacent
            // segments filtered out, to measure self-distance

            if (bResample) {
                SplitToMaxEdgeLength(graph, overlapRadius / 2);
                CollapseToMinEdgeLength(graph, overlapRadius / 3);
            }

            double dist_thresh = overlapRadius;
            double sharp_thresh_deg = 20;

            // 
            // Step 1: disconnect paths at 'sharp' corners, and erode one side until
            //   there is no overlap. We do this first because it guarantees we are
            //   going to erode one side from sharp corner and not the other.
            //

            // find "sharp" turns
            List<Vector2d> sharps = new List<Vector2d>();
            foreach (int vid in graph.VertexIndices()) {
                double open_angle = opening_angle(graph, vid);
                if (open_angle == double.MaxValue)
                    continue;
                if ( open_angle < sharp_thresh_deg ) 
                    sharps.Add(new Vector2d(vid, open_angle));
            }

            // sort by opening-angle
            sharps.Sort((a, b) => { return (a.y < b.y) ? -1 : (a.y > b.y ? 1 : 0); });

            // for each sharp vtx, pick a side and erode it until no-overlap vtx is found
            foreach ( Vector2d sharp in sharps ) {
                int vid = (int)sharp.x;
                if (graph.IsVertex(vid) == false)
                    continue;
                int initial_eid = graph.GetVtxEdges(vid)[0];
                decimate_forward(vid, initial_eid, graph, dist_thresh);
            }


            //
            // Step 2: find any other possible self-overlaps and erode them.
            //

            // sort all vertices by opening angle. For any overlap, we can erode
            // on either side. Prefer to erode on side with higher curvature.
            List<Vector2d> remaining_v = new List<Vector2d>(graph.MaxVertexID);
            foreach ( int vid in graph.VertexIndices()) {
                double open_angle = opening_angle(graph, vid);
                if (open_angle == double.MaxValue)
                    continue;
                remaining_v.Add(new Vector2d(vid, open_angle));
            }
            remaining_v.Sort((a, b) => { return (a.y < b.y) ? -1 : (a.y > b.y ? 1 : 0); });


            // look for overlap vertices. When we find one, erode on both sides.
            foreach (Vector2d vinfo in remaining_v) {
                int vid = (int)vinfo.x;
                if (graph.IsVertex(vid) == false)
                    continue;
                double dist = MinSelfSegDistance_Robust(vid, graph, 2*dist_thresh);
                if (dist < dist_thresh) {
                    List<int> nbrs = new List<int>(graph.GetVtxEdges(vid));
                    foreach ( int eid in nbrs ) 
                        decimate_forward(vid, eid, graph, dist_thresh);
                }
            }



            CollapseFlatVertices(graph, 2.5);

        }



        static double opening_angle(DGraph2 graph, int vid)
        {
            if (graph.GetVtxEdgeCount(vid) != 2)
                return double.MaxValue;

            Index2i nbrv = Index2i.Zero;
            int i = 0;
            foreach (int nbrvid in graph.VtxVerticesItr(vid))    // [TODO] faster way to get these? Index4i return?
                nbrv[i++] = nbrvid;

            Vector2d v = graph.GetVertex(vid);
            double open_angle = Vector2d.AngleD(
                (graph.GetVertex(nbrv.a) - v).Normalized,
                (graph.GetVertex(nbrv.b) - v).Normalized);
            return open_angle;
        }



        static void decimate_forward(int start_vid, int first_eid, DGraph2 graph, double dist_thresh)
        {
            int cur_eid = first_eid;
            int cur_vid = start_vid;

            bool stop = false;
            while (!stop) {
                Index2i nextinfo = next_edge_and_vtx(cur_eid, cur_vid, graph);
                if (nextinfo == Index2i.Max)
                    break;

                graph.RemoveEdge(cur_eid, true);
                cur_eid = nextinfo.a;
                cur_vid = nextinfo.b;

                //double dist = MinSelfSegDistance(cur_vid, graph);
                double dist = MinSelfSegDistance_Robust(cur_vid, graph, 2 * dist_thresh);
                if (dist > dist_thresh)
                    stop = true;
            }

        }


        static Index2i next_edge_and_vtx(int eid, int prev_vid, DGraph2 graph)
        {
            Index2i ev = graph.GetEdgeV(eid);
            int next_vid = (ev.a == prev_vid) ? ev.b : ev.a;

            if (graph.GetVtxEdgeCount(next_vid) != 2)
                return Index2i.Max;

            foreach ( int next_eid in graph.VtxEdgesItr(next_vid)) {
                if (next_eid != eid)
                    return new Index2i(next_eid, next_vid);
            }
            return Index2i.Max;
}



        static double MinSelfVtxDistance(int vid, DGraph2 graph)
        {
            Vector2d pos = graph.GetVertex(vid);
            List<int> nbrs = new List<int>(graph.VtxVerticesItr(vid));
            nbrs.Add(vid);

            double min_dist_sqr = double.MaxValue;
            foreach ( int id in graph.VertexIndices()) {
                Vector2d v = graph.GetVertex(id);
                double d = v.DistanceSquared(pos);
                if ( d < min_dist_sqr && nbrs.Contains(id) == false ) {
                    min_dist_sqr = d;
                }
            }

            return Math.Sqrt(min_dist_sqr);
        }



        static double MinSelfSegDistance(int vid, DGraph2 graph)
        {
            Vector2d pos = graph.GetVertex(vid);

            double min_dist_sqr = double.MaxValue;
            foreach ( int eid in graph.EdgeIndices()) {
                Index2i ev = graph.GetEdgeV(eid);
                if (ev.a == vid || ev.b == vid)
                    continue;

                Segment2d seg = new Segment2d(graph.GetVertex(ev.a), graph.GetVertex(ev.b));
                double d = seg.DistanceSquared(pos);
                if (d < min_dist_sqr) {
                    min_dist_sqr = d;
                }
            }

            return Math.Sqrt(min_dist_sqr);
        }



        static double MinSelfSegDistance_Robust(int vid, DGraph2 graph, double self_radius)
        {
            Vector2d pos = graph.GetVertex(vid);

            List<int> ignore_edges = FindConnectedEdgesInRadius(vid, graph, self_radius);

            double min_dist_sqr = double.MaxValue;
            foreach (int eid in graph.EdgeIndices()) {
                Index2i ev = graph.GetEdgeV(eid);
                if (ev.a == vid || ev.b == vid)
                    continue;
                if (ignore_edges.Contains(eid))
                    continue;

                Segment2d seg = new Segment2d(graph.GetVertex(ev.a), graph.GetVertex(ev.b));
                double d = seg.DistanceSquared(pos);
                if (d < min_dist_sqr) {
                    min_dist_sqr = d;
                }
            }

            return Math.Sqrt(min_dist_sqr);
        }



        static List<int> FindConnectedEdgesInRadius(int vid, DGraph2 graph, double rad)
        {
            Vector2d pos = graph.GetVertex(vid);

            List<int> edges = new List<int>();

            foreach ( int eid in graph.VtxEdgesItr(vid) ) {
                edges.Add(eid);

                Index2i next = new Index2i(eid, vid);
                while(true) {
                    next = next_edge_and_vtx(next.a, next.b, graph);
                    if (next.a == eid)
                        goto looped;   // looped! we can exit now w/o next step of outer loop
                    if (next == Index2i.Max)
                        break;
                    edges.Add(next.a);
                    if (pos.Distance(graph.GetVertex(next.b)) > rad)
                        break;
                }
            }
            looped:
            return edges;
        }



        public static void TestDGraph2()
		{
			Window window = new Window("TestDGraph2");
			window.SetDefaultSize(600, 600);
			window.SetPosition(WindowPosition.Center);

			GeneralPolygon2d poly = new GeneralPolygon2d(
				Polygon2d.MakeCircle(10, 32));
			Polygon2d hole = Polygon2d.MakeCircle(5, 32);
			hole.Translate(new Vector2d(2, 0));
			hole.Reverse();
			poly.AddHole(hole);

			double step_size = 0.2;
			double offset = 2;
			double spacing = 0.2;
			int nsteps = (int)(offset / step_size);
			DGraph2 graph = Offset(poly, offset, nsteps, spacing);



			DebugViewCanvas view = new DebugViewCanvas();
			view.AddPolygon(poly, Colorf.Orange);
			view.AddGraph(graph, Colorf.Blue);
			window.Add(view);
			window.ShowAll();
		}




		public static DGraph2 Offset(GeneralPolygon2d poly, double fOffset, int nSteps, double fMergeThresh) {
			double dt = fOffset / nSteps;

			DGraph2 graph = new DGraph2();
			graph.AppendPolygon(poly.Outer);
			foreach (var h in poly.Holes)
				graph.AppendPolygon(h);

			SplitToMaxEdgeLength(graph, fMergeThresh * 1.5);

			for (int i = 0; i < nSteps; ++i ) {

				foreach ( int vid in graph.VertexIndices() ) {
					Vector2d cur_pos = graph.GetVertex(vid);
					int iHole, iSeg; double segT;
					double distSqr =
						poly.DistanceSquared(cur_pos, out iHole, out iSeg, out segT);
					double dist = Math.Sqrt(distSqr);
					Vector2d normal = poly.GetNormal(iSeg, segT, iHole);

					// sign??
					//Vector2d nearPt = poly.PointAt(iSeg, segT, iHole);
					//double dir_dot = (cur_pos - nearPt).Dot(normal);
					//double sign = (dir_dot == 0) ? 1 : Math.Sin(dir_dot);


					// somehow need to reduce step size as we converge on zero.
					// 

					double step = dt;
					if (dist > fOffset) {
						step = Math.Max(fOffset - dist, -step);
					} else {
						step = Math.Min(fOffset-dist, step);
					}

					Vector2d new_pos = cur_pos - step * normal;
					graph.SetVertex(vid, new_pos);
				}

				SmoothPass(graph, 5, 0.2, fMergeThresh / 2);
				int joined = 0;
				do {
					joined = JoinInTolerance(graph, fMergeThresh);
				} while (joined > 0);
				CollapseToMinEdgeLength(graph, fMergeThresh);
				SplitToMaxEdgeLength(graph, fMergeThresh * 1.5);
			}

			//SmoothPass(graph, 25, 0.1, fMergeThresh);


			return graph;
		}



		public static void SmoothPass(DGraph2 graph, int passes, double smooth_alpha, double max_move)
		{
			double max_move_sqr = max_move * max_move;
			int NV = graph.MaxVertexID;
			Vector2d[] smoothedV = new Vector2d[NV];

			for (int pi = 0; pi < passes; ++pi) {
				for (int vid = 0; vid < NV; ++vid) {
					if (!graph.IsVertex(vid))
						continue;
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
				}

				for (int vid = 0; vid < NV; ++vid) {
					if (graph.IsVertex(vid))
						graph.SetVertex(vid, smoothedV[vid]);
				}
			}

		}



		public static int JoinInTolerance(DGraph2 graph, double fMergeDist) {
			double mergeSqr = fMergeDist * fMergeDist;

			int merged = 0;
			int NV = graph.MaxVertexID;
			for (int a = 0; a < NV; ++a) {
				if (!graph.IsVertex(a))
					continue;
				Vector2d va = graph.GetVertex(a);

				int bNearest = -1;
				double nearDistSqr = double.MaxValue;
				for (int b = a + 1; b < NV; ++b) {
					if (!graph.IsVertex(b))
						continue;
					double distsqr = va.DistanceSquared(graph.GetVertex(b));
					if (distsqr < mergeSqr && distsqr < nearDistSqr) {
						if (graph.FindEdge(a, b) == DGraph2.InvalidID) {
							nearDistSqr = distsqr;
							bNearest = b;
						}
					}
				}
				if (bNearest == -1)
					continue;

				int eid = graph.AppendEdge(a, bNearest);
				DGraph2.EdgeCollapseInfo collapseInfo;
				graph.CollapseEdge(bNearest, a, out collapseInfo);
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
                            double opena = Math.Abs(opening_angle(graph, ev.a));
                            double openb = Math.Abs(opening_angle(graph, ev.b));
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

                        DGraph2.EdgeCollapseInfo collapseInfo;
                        if (graph.CollapseEdge(keep, remove, out collapseInfo) == MeshResult.Ok) {
                            graph.SetVertex(collapseInfo.vKept, newPos);
                            done = false;
                        }
                    }

                } while (cur_eid != 0);
			}
		}












        public static void CollapseFlatVertices(DGraph2 graph, double fMaxDeviationDeg = 5)
        {
            bool done = false;
            int max_passes = 200;
            int pass_count = 0;
            while (done == false && pass_count++ < max_passes) {
                done = true;

                // [RMS] do modulo-indexing here to avoid pathological cases where we do things like
                // continually collapse a short edge adjacent to a long edge (which will result in crazy over-collapse)
                int N = graph.MaxVertexID;
                const int nPrime = 31337;     // any prime will do...
                int cur_vid = 0;
                do {
                    int vid = cur_vid;
                    cur_vid = (cur_vid + nPrime) % N;

                    if (!graph.IsVertex(vid))
                        continue;
                    if (graph.GetVtxEdgeCount(vid) != 2)
                        continue;

                    double open = Math.Abs(opening_angle(graph, vid));
                    if (open < 180 - fMaxDeviationDeg)
                        continue;

                    int eid = graph.GetVtxEdges(vid).First();

                    Index2i ev = graph.GetEdgeV(eid);
                    int other_v = (ev.a == vid) ? ev.b : ev.a;

                    DGraph2.EdgeCollapseInfo collapseInfo;
                    MeshResult result = graph.CollapseEdge(other_v, vid, out collapseInfo);
                    if ( result == MeshResult.Ok) {
                        done = false;
                    } else {
                        System.Console.WriteLine("wha?");
                    }

                } while (cur_vid != 0);
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
					if (graph.SplitEdge(eid, out splitInfo) == MeshResult.Ok && dist > 2 * fMaxLen) {
						queue.Add(eid);
						queue.Add(splitInfo.eNewBN);
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
					if (graph.SplitEdge(eid, out splitInfo) == MeshResult.Ok && dist > 2 * fMaxLen) {
						queue.Add(eid);
						queue.Add(splitInfo.eNewBN);
					}
				}				
			}
		}


	}
}
