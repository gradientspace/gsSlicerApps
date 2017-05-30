using System;
using System.Collections.Generic;
using Gtk;
using GLib;
using SkiaSharp;
using g3;
using gs;

namespace SliceViewer 
{
	class SliceViewCanvas : DrawingArea
	{


		public bool ShowOpenEndpoints = true;

		public float Zoom = 0.95f;

		// this is a pixel-space translate
		public Vector2f Translate = Vector2f.Zero;


		public enum NumberModes
		{
			NoNumbers,
			PathNumbers
		}
		NumberModes num_mode = NumberModes.NoNumbers;
		public NumberModes NumberMode {
			get { return num_mode; }
			set { num_mode = value; QueueDraw(); }
		}


		bool show_below = true;
		public bool ShowBelowLayer {
			get { return show_below; }
			set { show_below = value; QueueDraw(); }
		}


		public SliceViewCanvas() 
		{
			ExposeEvent += OnExpose;

			ButtonPressEvent += OnButtonPressEvent;
			ButtonReleaseEvent += OnButtonReleaseEvent;
			MotionNotifyEvent += OnMotionNotifyEvent;
			ScrollEvent += OnScrollEvent;
			Events = Gdk.EventMask.ExposureMask | Gdk.EventMask.LeaveNotifyMask |
			            Gdk.EventMask.ButtonPressMask | Gdk.EventMask.ButtonReleaseMask | Gdk.EventMask.PointerMotionMask |
			            Gdk.EventMask.ScrollMask;

			SetPaths(new PathSet());
			InitializeInternals();
		}

		PathSet Paths;
		LayersDetector Layers;
		int currentLayer = 0;
		Func<Vector3d, byte> LayerFilterF = (v) => { return 255; };

		public void SetPaths(PathSet paths) {
			Paths = paths;
			Layers = new LayersDetector(Paths);
			CurrentLayer = 0;
		}

		public int CurrentLayer {
			get { return currentLayer; }
			set {
				currentLayer = MathUtil.Clamp(value, 0, Layers.Layers - 1);
				Interval1d layer_zrange = Layers.GetLayerZInterval(currentLayer);
				Interval1d prev_zrange = Layers.GetLayerZInterval(currentLayer - 1);
				LayerFilterF = (v) => {
					if (layer_zrange.Contains(v.z))
						return 255;
					else if (show_below && prev_zrange.Contains(v.z))
						return 16;
					return (byte)0;
				};
				QueueDraw();
			}
		}



		    
		SKPath MakePath<T>(LinearPath3<T> path, Func<Vector2d, SKPoint> mapF) where T : IPathVertex
		{
			SKPath p = new SKPath();
			p.MoveTo(mapF(path[0].Position.xy));
			for ( int i = 1; i < path.VertexCount; i++ )
				p.LineTo( mapF(path[i].Position.xy) );
			return p;
		}




		Func<Vector2d, Vector2f> SceneXFormF = (v) => { return (Vector2f)v; };
		Func<Vector2d, SKPoint> SceneToSkiaF = null;
		void InitializeInternals()
		{
			SceneToSkiaF = (v) => {
				Vector2f p = SceneXFormF(v);
				return new SKPoint(p.x, p.y);
			};
		}
			
			


		void OnExpose(object sender, ExposeEventArgs args)
		{
			DrawingArea area = (DrawingArea) sender;
			Cairo.Context cr =  Gdk.CairoHelper.Create(area.GdkWindow);

			int width = area.Allocation.Width;
			int height = area.Allocation.Height;

			//AxisAlignedBox3d bounds3 = Paths.Bounds;
			AxisAlignedBox3d bounds3 = Paths.ExtrudeBounds;
			AxisAlignedBox2d bounds = (bounds3 == AxisAlignedBox3d.Empty) ?
				new AxisAlignedBox2d(0, 0, 500, 500) : 
				new AxisAlignedBox2d(bounds3.Min.x, bounds3.Min.y, bounds3.Max.x, bounds3.Max.y );

			double sx = (double)width / bounds.Width;
			double sy = (double)height / bounds.Height;

			float scale = (float)Math.Min(sx, sy);

			// we apply this translate after scaling to pixel coords
			Vector2f pixC = Zoom * scale * (Vector2f)bounds.Center;
			Vector2f translate = new Vector2f(width/2, height/2) - pixC;

			using (var bitmap = new SKBitmap(width, height, SkiaUtil.ColorType(), SKAlphaType.Premul))
			{
				IntPtr len;
				using (var skSurface = SKSurface.Create(bitmap.Info.Width, bitmap.Info.Height, SkiaUtil.ColorType(), SKAlphaType.Premul, bitmap.GetPixels(out len), bitmap.Info.RowBytes))
				{
					var canvas = skSurface.Canvas;
					canvas.Clear(SkiaUtil.Color(255, 255, 255, 255));

					// update scene xform
					SceneXFormF = (pOrig) => {
						Vector2f pNew = (Vector2f)pOrig - (Vector2f)bounds.Center;
						pNew = Zoom * scale * pNew;
						pNew += (Vector2f)pixC;
						pNew += translate + Zoom*Translate;
						pNew.y = canvas.ClipBounds.Height - pNew.y;
						return pNew;
					};


					using (var paint = new SKPaint())
					{
						paint.IsAntialias = true;

						paint.StrokeWidth = 1;
                        paint.Style = SKPaintStyle.Stroke;

						DrawLayerPaths(Paths, canvas, paint);

						if (NumberMode == NumberModes.PathNumbers )
							DrawPathLabels(Paths, canvas, paint);
					}

					Cairo.Surface surface = new Cairo.ImageSurface(
						bitmap.GetPixels(out len),
						Cairo.Format.Argb32,
						bitmap.Width, bitmap.Height,
						bitmap.Width * 4);

					surface.MarkDirty();
					cr.SetSourceSurface(surface, 0, 0);
					cr.Paint();
				}
			}

			//return true;
		}






		void ProcessLinearPaths(PathSet pathSetIn, Action<LinearPath3<PathVertex>> processF) {
			Action<IPath> drawPath = (path) => {
				if (path is LinearPath3<PathVertex>)
					processF(path as LinearPath3<PathVertex>);
				// else we might have other path type...
			};
			Action<IPathSet> drawPaths = null;
			drawPaths = (pathList) => {
				foreach (IPath path in pathList) {
					if (path is IPathSet)
						drawPaths(path as IPathSet);
					else
						drawPath(path);
				}
			};

			drawPaths(pathSetIn);			
		}




		private void DrawLayerPaths(PathSet pathSetIn, SKCanvas canvas, SKPaint paint) {

			SKColor extrudeColor = SkiaUtil.Color(0, 0, 0, 255);
			SKColor travelColor = SkiaUtil.Color(0, 255, 0, 128);
			SKColor startColor = SkiaUtil.Color(255, 0, 0, 128);
			SKColor planeColor = SkiaUtil.Color(0, 0, 255, 128);
			float pointR = 3f;

			Action<LinearPath3<PathVertex>> drawPath3F = (polyPath) => {

				Vector3d v0 = polyPath.Start.Position;
				byte layer_alpha = LayerFilterF(v0);
				if (layer_alpha == 0)
					return;
				bool is_below = (layer_alpha < 255);

				SKPath path = MakePath(polyPath, SceneToSkiaF);
				if (polyPath.Type == PathTypes.Deposition) {
					paint.Color = extrudeColor;
					paint.StrokeWidth = 1.5f;
				} else if (polyPath.Type == PathTypes.Travel) {
					if (is_below)
						return;
					paint.Color = travelColor;
					paint.StrokeWidth = 3;
				} else if (polyPath.Type == PathTypes.PlaneChange) {
					if (is_below)
						return;					
					paint.StrokeWidth = 0.5f;
					paint.Color = planeColor;
				} else {
					if (is_below)
						return;					
					paint.Color = startColor;
				}
				paint.Color = SkiaUtil.Color(paint.Color, layer_alpha);

				if (is_below)
					paint.StrokeWidth = 6;
				canvas.DrawPath(path, paint);

				paint.StrokeWidth = 1;

				if (is_below == false) {
					Vector2f pt = SceneXFormF(polyPath.Start.Position.xy);
					if (polyPath.Type == PathTypes.Deposition) {
						canvas.DrawCircle(pt.x, pt.y, pointR, paint);
					} else if (polyPath.Type == PathTypes.Travel) {
						canvas.DrawCircle(pt.x, pt.y, pointR, paint);
					} else if (polyPath.Type == PathTypes.PlaneChange) {
						paint.Style = SKPaintStyle.Fill;
						canvas.DrawCircle(pt.x, pt.y, 4f, paint);
						paint.Style = SKPaintStyle.Stroke;
					}
				}
			};

			ProcessLinearPaths(pathSetIn, drawPath3F);
			
		}





		private void DrawPathLabels(PathSet pathSetIn, SKCanvas canvas, SKPaint paint) 
		{
			int counter = 1;

			paint.Style = SKPaintStyle.StrokeAndFill;
			paint.IsAntialias = true;
			paint.Color = SKColors.Black;
			paint.StrokeWidth = 1;
			paint.TextSize = 15.0f;

			Action<LinearPath3<PathVertex>> drawLabelF = (polyPath) => {
				Vector3d v0 = polyPath.Start.Position;
				byte layer_alpha = LayerFilterF(v0);
				if (layer_alpha < 255)
					return;

				string label = string.Format("{0}", counter++);
				paint.Color = SKColors.Black;

				Vector3d vPos = v0;
				float shiftX = 0, shiftY = 0;
				if ( polyPath.Type == PathTypes.Travel ) {
					vPos = (polyPath.Start.Position + polyPath.End.Position) * 0.5;
				} else if ( polyPath.Type == PathTypes.PlaneChange ) {
					shiftY = paint.TextSize * 0.5f;
					//shiftX = -paint.TextSize * 0.5f;
					paint.Color = SKColors.DarkRed;
				}

				SKPoint pos = SceneToSkiaF(vPos.xy);
				canvas.DrawText(label, pos.X + shiftX, pos.Y + shiftY, paint);
			};

			ProcessLinearPaths(pathSetIn, drawLabelF);
		}






		// zoom
		private void OnScrollEvent(object o, ScrollEventArgs args)
		{
			if (args.Event.Direction == Gdk.ScrollDirection.Up)
				Zoom *= 1.05f;
			else
				Zoom = Math.Max(0.25f, Zoom * (1.0f / 1.05f));
			QueueDraw();
		}


		// pan support
		bool left_down = false;
		Vector2f start_pos = Vector2f.Zero;
		Vector2f pan_start;
		private void OnButtonPressEvent(object o, ButtonPressEventArgs args)
		{
			if (args.Event.Button == 1) {
				left_down = true;
				start_pos = new Vector2f((float)args.Event.X, (float)args.Event.Y);
				pan_start = Translate;
			}
		}
		private void OnButtonReleaseEvent(object o, ButtonReleaseEventArgs args)
		{
			if (left_down)
				left_down = false;
		}
		private void OnMotionNotifyEvent(object o, MotionNotifyEventArgs args)
		{
			if (left_down) {
				Vector2f cur_pos = new Vector2f((float)args.Event.X, (float)args.Event.Y);
				Vector2f delta = cur_pos - start_pos;
				delta.y = -delta.y;
				delta *= 1.0f;  // speed
				Translate = pan_start + delta / Zoom;
				QueueDraw();
			}
		}


		void Reset()
		{
			Zoom = 1.0f;
			Translate = Vector2f.Zero;
			QueueDraw();
		}









	}





}
