# gsSlicerApps

command line and interactive front-ends to [gsSlicer](https://github.com/gradientspace/gsSlicer)

C#, MIT License (but see note about dependencies below). Copyright 2017 ryan schmidt / gradientspace

questions? get in touch on twitter: @rms80 or @gradientspace, or email rms@gradientspace.com.

# Overview

The intention of this project is that it will provide GUI and command-line access to gsSlicer. At present this project is mainly used for testing and debugging of gsSlicer. 



# sliceViewGTK

2D viewer for GCode files. Current code will also slice and toolpath a mesh file on startup. Path is currently hardcoded in **SliceViewerMain**. 

# dlpView

basic 2D tool for slicing a mesh and generating/browsing per-layer bitmaps, suitable for DLP printing. Path is currently hardcoded in **DLPViewerMain**. 



# Dependencies

The **sliceViewGTK** and **dlpView** code is MIT-license, but they depends on Gdk/Gtk/GtkSharp (LGPL) and the SkiaSharp wrapper (MIT) around the Skia library (BSD).  
