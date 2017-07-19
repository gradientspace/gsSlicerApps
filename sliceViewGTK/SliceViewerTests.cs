using System;
using System.Collections.Generic;
using Gtk;
using GLib;
using g3;

namespace SliceViewer
{
	public static class SliceViewerTests
	{
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

			SplitToTolerance(graph, fMergeThresh * 1.5);

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
				DecimateToTolerance(graph, fMergeThresh);
				SplitToTolerance(graph, fMergeThresh * 1.5);
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



		public static void DecimateToTolerance(DGraph2 graph, double fMinLen) {
			double minLenSqr = fMinLen * fMinLen;
			int NE = graph.MaxEdgeID;
			bool done = false;
			while (!done) {
				done = true;
				for (int eid = 0; eid < NE; ++eid) {
					if (!graph.IsEdge(eid))
						continue;
					Index2i ev = graph.GetEdgeV(eid);
					Vector2d va = graph.GetVertex(ev.a);
					Vector2d vb = graph.GetVertex(ev.b);
					double dist = va.DistanceSquared(vb);
					if (dist < minLenSqr) {
						DGraph2.EdgeCollapseInfo collapseInfo;
						if (graph.CollapseEdge(ev.a, ev.b, out collapseInfo) == MeshResult.Ok) {
							done = false;
						}
					}
				}
			}
		}



		public static void SplitToTolerance(DGraph2 graph, double fMaxLen) {
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
