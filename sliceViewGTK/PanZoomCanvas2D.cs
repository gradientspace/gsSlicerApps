using System;
using System.Collections.Generic;
using Gtk;
using GLib;
using SkiaSharp;
using g3;


namespace gs
{
	public class PanZoomCanvas2D : DrawingArea
	{
		public float Zoom = 0.95f;

		// this is a pixel-space translate
		public Vector2f PixelTranslate = Vector2f.Zero;

		public PanZoomCanvas2D()
		{
			ExposeEvent += OnExpose;

			ButtonPressEvent += OnButtonPressEvent;
			ButtonReleaseEvent += OnButtonReleaseEvent;
			MotionNotifyEvent += OnMotionNotifyEvent;
			ScrollEvent += OnScrollEvent;
			Events = Gdk.EventMask.ExposureMask | Gdk.EventMask.LeaveNotifyMask |
						Gdk.EventMask.ButtonPressMask | Gdk.EventMask.ButtonReleaseMask | Gdk.EventMask.PointerMotionMask |
						Gdk.EventMask.ScrollMask;
			
		}



		protected virtual AxisAlignedBox2d DrawingBounds {
			get { return new AxisAlignedBox2d(0, 0, 500, 500); }
		}

		protected virtual void DrawScene(SKCanvas drawCanvas, Func<Vector2d, Vector2f> ViewTransformF) {
		}



		void OnExpose(object sender, ExposeEventArgs args)
		{
			DrawingArea area = (DrawingArea)sender;
			Cairo.Context cr = Gdk.CairoHelper.Create(area.GdkWindow);

			int width = area.Allocation.Width;
			int height = area.Allocation.Height;

			AxisAlignedBox2d bounds = DrawingBounds;

			double sx = (double)width / bounds.Width;
			double sy = (double)height / bounds.Height;

			float scale = (float)Math.Min(sx, sy);

			// we apply this translate after scaling to pixel coords
			Vector2f pixC = Zoom * scale * (Vector2f)bounds.Center;
			Vector2f translate = new Vector2f(width / 2, height / 2) - pixC;


			using (var bitmap = new SKBitmap(width, height, SkiaUtil.ColorType(), SKAlphaType.Premul)) {
				IntPtr len;
				using (var skSurface = SKSurface.Create(bitmap.Info.Width, bitmap.Info.Height, SkiaUtil.ColorType(), SKAlphaType.Premul, bitmap.GetPixels(out len), bitmap.Info.RowBytes)) {
					var canvas = skSurface.Canvas;

					// update scene xform
					Func<Vector2d, Vector2f> ViewTransformF = (pOrig) => {
						Vector2f pNew = (Vector2f)pOrig - (Vector2f)bounds.Center;
						pNew = Zoom * scale * pNew;
						pNew += (Vector2f)pixC;
						pNew += translate + Zoom * PixelTranslate;
						pNew.y = canvas.ClipBounds.Height - pNew.y;
						return pNew;
					};

					DrawScene(canvas, ViewTransformF);

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





		// zoom
		protected void OnScrollEvent(object o, ScrollEventArgs args)
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
		protected void OnButtonPressEvent(object o, ButtonPressEventArgs args)
		{
			if (args.Event.Button == 1) {
				left_down = true;
				start_pos = new Vector2f((float)args.Event.X, (float)args.Event.Y);
				pan_start = PixelTranslate;
			}
		}
		protected void OnButtonReleaseEvent(object o, ButtonReleaseEventArgs args)
		{
			if (left_down)
				left_down = false;
		}
		protected void OnMotionNotifyEvent(object o, MotionNotifyEventArgs args)
		{
			if (left_down) {
				Vector2f cur_pos = new Vector2f((float)args.Event.X, (float)args.Event.Y);
				Vector2f delta = cur_pos - start_pos;
				delta.y = -delta.y;
				delta *= 1.0f;  // speed
				PixelTranslate = pan_start + delta / Zoom;
				QueueDraw();
			}
		}


		public void Reset()
		{
			Zoom = 1.0f;
			PixelTranslate = Vector2f.Zero;
			QueueDraw();
		}



	}
}
