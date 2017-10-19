using System;
using System.Collections.Generic;
using SkiaSharp;
using g3;
using gs;

namespace SliceViewer
{
	public class DebugViewCanvas : PanZoomCanvas2D
	{
		List<DGraph2> Graphs = new List<DGraph2>();
		List<GeneralPolygon2d> GPolygons = new List<GeneralPolygon2d>();
		Dictionary<object, Colorf> Colors = new Dictionary<object, Colorf>();
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



	}
}
