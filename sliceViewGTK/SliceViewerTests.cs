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

            PathOverlapRepair repair = new PathOverlapRepair() { OverlapRadius = 0.4f };
            repair.Add(offset);
            repair.Compute();
            DGraph2 graph = repair.GetResultGraph();


            PathOverlapRepair outer_repair = new PathOverlapRepair() { OverlapRadius = 0.4f };
            outer_repair.Add(outer);
            outer_repair.Compute();
            DGraph2 outer_graph = outer_repair.GetResultGraph();


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
			DGraph2 graph = Offset(poly, offset, nsteps, spacing, true);


            DGraph2Util.Curves c = DGraph2Util.ExtractCurves(graph);

            DebugViewCanvas view = new DebugViewCanvas();
			view.AddPolygon(poly, Colorf.Orange);
			view.AddGraph(graph, Colorf.Blue);
			window.Add(view);
			window.ShowAll();
		}




		public static DGraph2 Offset(GeneralPolygon2d poly, double fOffset, int nSteps, double fMergeThresh, bool bResolveOverlaps) {
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

				SmoothPass(graph, 25, 0.2, fMergeThresh / 2);
				int joined = 0;
				do {
					joined = JoinInTolerance(graph, fMergeThresh);
				} while (joined > 0);
				CollapseToMinEdgeLength(graph, fMergeThresh);
				SplitToMaxEdgeLength(graph, fMergeThresh * 1.5);
			}

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

                    double open = Math.Abs(graph.OpeningAngle(vid));
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
