using System;
using System.IO;
using g3;
using gs;
using gs.info;

namespace MeshToGCodeDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            CappedCylinderGenerator cylgen = new CappedCylinderGenerator() {
                BaseRadius = 10, TopRadius = 5, Height = 20, Slices = 32
            };
            DMesh3 mesh = cylgen.Generate().MakeDMesh();
            MeshTransforms.ConvertYUpToZUp(mesh);       // g3 meshes are usually Y-up

            // center mesh above origin
            AxisAlignedBox3d bounds = mesh.CachedBounds;
            Vector3d baseCenterPt = bounds.Center - bounds.Extents.z*Vector3d.AxisZ;
            MeshTransforms.Translate(mesh, -baseCenterPt);

            // create print mesh set
            PrintMeshAssembly meshes = new PrintMeshAssembly();
            meshes.AddMesh(mesh, PrintMeshOptions.Default);

            // create settings
            //MakerbotSettings settings = new MakerbotSettings(Makerbot.Models.Replicator2);
            //PrintrbotSettings settings = new PrintrbotSettings(Printrbot.Models.Plus);
            //MonopriceSettings settings = new MonopriceSettings(Monoprice.Models.MP_Select_Mini_V2);
            RepRapSettings settings = new RepRapSettings(RepRap.Models.Unknown);

            // do slicing
            MeshPlanarSlicer slicer = new MeshPlanarSlicer() {
                LayerHeightMM = settings.LayerHeightMM };
            slicer.Add(meshes);
            PlanarSliceStack slices = slicer.Compute();

            // run print generator
            SingleMaterialFFFPrintGenerator printGen =
                new SingleMaterialFFFPrintGenerator(meshes, slices, settings);
            if ( printGen.Generate() ) {
                // export gcode
                GCodeFile gcode = printGen.Result;
                using (StreamWriter w = new StreamWriter("c:\\demo\\cone.gcode")) {
                    StandardGCodeWriter writer = new StandardGCodeWriter();
                    writer.WriteFile(gcode, w);
                }
            }
        }
    }
}
