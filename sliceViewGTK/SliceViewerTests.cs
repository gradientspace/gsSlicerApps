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


        public static void TestFill()
        {
            Window window = new Window("TestFill");
            window.SetDefaultSize(600, 600);
            window.SetPosition(WindowPosition.Center);

            DebugViewCanvas view = new DebugViewCanvas();

            GeneralPolygon2d poly = new GeneralPolygon2d(
                Polygon2d.MakeCircle(20, 32));

            Polygon2d hole = Polygon2d.MakeCircle(15, 32);
            hole.Reverse();
            poly.AddHole(hole);
            
            view.AddPolygon(poly, Colorf.Black);

            double spacing = 0.5;

            double[] offsets = new double[] { 5 };

            foreach (double offset in offsets) {
                DGraph2 graph = TopoOffset2d.QuickCompute(poly, offset, spacing);
                DGraph2Util.Curves c = DGraph2Util.ExtractCurves(graph);
                view.AddGraph(graph, Colorf.Red);

                //DGraph2 perturbGraph = perturb_fill(graph, poly, 5.0f, spacing);
                DGraph2 perturbGraph = perturb_fill_2(graph, poly, 2.5f, spacing);
                //DGraph2Util.Curves c2 = DGraph2Util.ExtractCurves(perturbGraph);
                view.AddGraph(perturbGraph, Colorf.Orange);

            }

            window.Add(view);
            window.ShowAll();
        }





        public static DGraph2 perturb_fill(DGraph2 graphIn, GeneralPolygon2d bounds, double waveWidth, double stepSize)
        {
            DGraph2Util.Curves curves = DGraph2Util.ExtractCurves(graphIn);
            Polygon2d poly = curves.Loops[0];

            GeneralPolygon2dBoxTree gpTree = new GeneralPolygon2dBoxTree(bounds);
            Polygon2dBoxTree outerTree = new Polygon2dBoxTree(bounds.Outer);
            Polygon2dBoxTree innerTree = new Polygon2dBoxTree(bounds.Holes[0]);

            DGraph2 graph = new DGraph2();
            graph.EnableVertexColors(Vector3f.Zero);

            double len = poly.Perimeter;
            int waves = (int)(len / waveWidth);
            double lenScale = len / (MathUtil.TwoPI * waves);
            double accum_len = 0;
            int prev_vid = -1, start_vid = -1;
            int N = poly.VertexCount;
            for ( int k = 0; k < N; ++k ) {
                double t = accum_len / lenScale;
                t = Math.Cos(t);
                //Vector2d normal = poly.GetNormal(k);
                Vector2d normal = poly[k].Normalized;
                int vid = graph.AppendVertex(poly[k], new Vector3f(t, normal.x, normal.y));
                if ( prev_vid != -1 ) {
                    graph.AppendEdge(prev_vid, vid);
                    accum_len += graph.GetVertex(prev_vid).Distance(graph.GetVertex(vid));
                } else {
                    start_vid = vid;
                }
                prev_vid = vid;
            }
            graph.AppendEdge(prev_vid, start_vid);

            Vector2d[] newPos = new Vector2d[graph.MaxVertexID];

            for (int k = 0; k < 10; ++k)
                smooth_pass(graph, 0.5f, newPos);

            for (int k = 0; k < 20; ++k) {

                foreach (int vid in graph.VertexIndices()) {
                    Vector2d v = graph.GetVertex(vid);
                    Vector3f c = graph.GetVertexColor(vid);

                    float t = c.x;
                    Vector2d n = new Vector2d(c.y, c.z);

                    if ( k == 0 || Math.Abs(t) > 0.9) {
                        v += t * stepSize * n;
                        if (!bounds.Contains(v)) {
                            v = gpTree.NearestPoint(v);
                        }
                    }

                    newPos[vid] = v;
                }

                foreach (int vid in graph.VertexIndices()) {
                    graph.SetVertex(vid, newPos[vid]);
                }

                for ( int j = 0; j < 5; ++j )
                    smooth_pass(graph, 0.1f, newPos);
            }

            return graph;
        }





        public static DGraph2 perturb_fill_2(DGraph2 graphIn, GeneralPolygon2d bounds, double waveWidth, double stepSize)
        {
            DGraph2Util.Curves curves = DGraph2Util.ExtractCurves(graphIn);
            Polygon2d poly = curves.Loops[0];

            GeneralPolygon2dBoxTree gpTree = new GeneralPolygon2dBoxTree(bounds);
            Polygon2dBoxTree outerTree = new Polygon2dBoxTree(bounds.Outer);
            Polygon2dBoxTree innerTree = new Polygon2dBoxTree(bounds.Holes[0]);

            DGraph2 graph = new DGraph2();
            graph.EnableVertexColors(Vector3f.Zero);

            graph.AppendPolygon(poly);

            DGraph2Resampler resampler = new DGraph2Resampler(graph);
            resampler.CollapseToMinEdgeLength(waveWidth);
            if (graph.VertexCount % 2 != 0) {
                // TODO smallest edge
                Index2i ev = graph.GetEdgeV(graph.EdgeIndices().First());
                DGraph2.EdgeCollapseInfo cinfo;
                graph.CollapseEdge(ev.a, ev.b, out cinfo);
            }


            // move to borders
            int startv = graph.VertexIndices().First();
            int eid = graph.VtxEdgesItr(startv).First();
            int curv = startv;
            bool outer = true;
            do {
                Polygon2dBoxTree use_tree = (outer) ? outerTree : innerTree;
                outer = !outer;
                graph.SetVertex(curv, use_tree.NearestPoint(graph.GetVertex(curv)));

                Index2i next = DGraph2Util.NextEdgeAndVtx(eid, curv, graph);
                eid = next.a;
                curv = next.b;
            } while (curv != startv);



            return graph;
        }




        public static void smooth_pass(DGraph2 graph, float alpha, Vector2d[] newPos)
        {
            foreach (int vid in graph.VertexIndices()) {
                Vector2d v = graph.GetVertex(vid);
                bool isvalid;
                Vector2d l = DGraph2Util.VertexLaplacian(graph, vid, out isvalid);
                v += alpha * l;
                newPos[vid] = v;
            }
            foreach (int vid in graph.VertexIndices()) {
                graph.SetVertex(vid, newPos[vid]);
            }
        }






        public static void TestDGraph2()
		{
			Window window = new Window("TestDGraph2");
			window.SetDefaultSize(600, 600);
			window.SetPosition(WindowPosition.Center);

            DebugViewCanvas view = new DebugViewCanvas();

            GeneralPolygon2d poly = new GeneralPolygon2d(
				Polygon2d.MakeCircle(10, 32));

            //Polygon2d hole = Polygon2d.MakeCircle(9, 32);
            //hole.Reverse();
            //poly.AddHole(hole);

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
            double[] offsets = new double[] { 0.2, 0.6 };

            TopoOffset2d o = new TopoOffset2d(poly) { PointSpacing = spacing };
            foreach (double offset in offsets) {
                o.Offset = offset;
                DGraph2 graph = o.Compute();
                DGraph2Util.Curves c = DGraph2Util.ExtractCurves(graph);
                view.AddGraph(graph, Colorf.Red);
            }



            window.Add(view);
			window.ShowAll();
		}


        






 



	}
}
