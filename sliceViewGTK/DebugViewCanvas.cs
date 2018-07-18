using System;
using System.Collections.Generic;
using SkiaSharp;
using System.IO;
using g3;
using gs;

namespace SliceViewer
{
	public class DebugViewCanvas : PanZoomCanvas2D
	{
		public List<DGraph2> Graphs = new List<DGraph2>();
        public List<GeneralPolygon2d> GPolygons = new List<GeneralPolygon2d>();
        public Dictionary<object, Colorf> Colors = new Dictionary<object, Colorf>();
		int timestamp = 0;

		Dictionary<object, SKPath> PathCache = new Dictionary<object, SKPath>();

		public DebugViewCanvas()
		{
		}


		public void AddGraph(DGraph2 graph, Colorf color) {
			Graphs.Add(graph);
			Colors.Add(graph, color);
			increment_timestamp();
		}

		public void AddPolygon(GeneralPolygon2d poly, Colorf color)
		{
			GPolygons.Add(poly);
			Colors.Add(poly, color);
			increment_timestamp();
		}

		void increment_timestamp() {
			timestamp++;
			PathCache.Clear();
			QueueDraw();
		}


		AxisAlignedBox2d cachedBounds;
		int boundsTimestamp = -1;

		protected override AxisAlignedBox2d DrawingBounds {
			get {
				if ( boundsTimestamp != timestamp ) {
					cachedBounds = AxisAlignedBox2d.Empty;
					foreach (var g in Graphs)
						cachedBounds.Contain(g.GetBounds());
					foreach (var gp in GPolygons)
						cachedBounds.Contain(gp.Bounds);
					boundsTimestamp = timestamp;
				}
				return cachedBounds;
			}
		}



		protected override void DrawScene(SKCanvas canvas, Func<Vector2d, Vector2f> ViewTransformF)
		{
			Random jitter = new Random(313377);
			Func<Vector2d> jitterF = () => { return Vector2d.Zero; };
			//{ return 0.1 * new Vector2d(jitter.NextDouble(), jitter.NextDouble()); };

			canvas.Clear(SkiaUtil.Color(255, 255, 255, 255));


			Func<Vector2d, SKPoint> SceneToSkiaF = (v) => {
				Vector2f p = ViewTransformF(v);
				return new SKPoint(p.x, p.y);
			};


			using (var paint = new SKPaint()) {
				paint.IsAntialias = true;

				paint.StrokeWidth = 1;
				paint.Style = SKPaintStyle.Stroke;

				foreach (var g in GPolygons)
					DrawPolygon(canvas, paint, g, SceneToSkiaF);
				foreach (var g in Graphs)
					DrawGraph(canvas, paint, g, SceneToSkiaF);
			}
		}



		void DrawPolygon(SKCanvas canvas, SKPaint paint, GeneralPolygon2d poly, Func<Vector2d, SKPoint> mapF)
		{
			Colorf color = Colorf.Red;
			if (Colors.ContainsKey(poly))
				color = Colors[poly];
			paint.Color = SkiaUtil.Color(color);

			SKPath path = SkiaUtil.ToSKPath(poly, mapF);
			paint.StrokeWidth = 2;
			canvas.DrawPath(path, paint);

			//paint.Color = SKColors.Orange;
			paint.StrokeWidth = 1;
			foreach (Vector2d v in poly.AllVerticesItr()) {
				SKPoint c = mapF(v);
				canvas.DrawCircle(c.X, c.Y, 3.0f, paint);
			}
		}



		void DrawGraph(SKCanvas canvas, SKPaint paint, DGraph2 graph, Func<Vector2d, SKPoint> mapF) {
			Colorf color = Colorf.Red;
			if (Colors.ContainsKey(graph))
				color = Colors[graph];
			paint.Color = SkiaUtil.Color(color);

			SKPath path = SkiaUtil.ToSKPath(graph, mapF);
			paint.StrokeWidth = 2;
			canvas.DrawPath(path, paint);

			paint.StrokeWidth = 1;
			//paint.Color = SKColors.Black;
			foreach (Vector2d v in graph.Vertices()) {
				SKPoint c = mapF(v);
				canvas.DrawCircle(c.X, c.Y, 3.0f, paint);
			}
		}





        public static void compute_distance_image()
        {
            DMesh3 mesh = StandardMeshReader.ReadMesh("c:\\scratch\\remesh.obj");
            MeshBoundaryLoops loops = new MeshBoundaryLoops(mesh);
            DCurve3 curve = loops[0].ToCurve();
            Polygon2d poly = new Polygon2d();
            foreach (Vector3d v in curve.Vertices)
                poly.AppendVertex(v.xy);
            int N = 1024;
            double cellsize = poly.Bounds.MaxDim / (double)N;
            Vector2d o = poly.Bounds.Min;
            o -= 4 * cellsize * Vector2d.One;
            N += 8;

            ShiftGridIndexer2 indexer = new ShiftGridIndexer2(poly.Bounds.Min, cellsize);

            double[] df = new double[N * N];
            double maxd = 0;
            for (int yi = 0; yi < N; ++yi) {
                for (int xi = 0; xi < N; ++xi) {
                    Vector2d p = indexer.FromGrid(new Vector2i(xi, yi));
                    double d = Math.Sqrt(poly.DistanceSquared(p));
                    df[yi * N + xi] = d;
                    maxd = Math.Max(d, maxd);
                }
            }

            SKBitmap bmp = new SKBitmap(N, N);
            for (int yi = 0; yi < N; ++yi) {
                for (int xi = 0; xi < N; ++xi) {
                    double d = df[yi*N + xi];
                    float f = (float)(d / maxd);
                    byte b = (byte)(int)(f * 255);
                    bmp.SetPixel(xi, yi, new SKColor(b, b, b));
                }
            }

            using (var image = SKImage.FromBitmap(bmp))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 80)) {
                    // save the data to a stream
                    using (var stream = File.OpenWrite("c:\\scratch\\distances.png")) {
                        data.SaveTo(stream);
                    }
            }

        }


	}
}
