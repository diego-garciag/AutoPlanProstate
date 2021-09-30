using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

// TODO: Replace the following version attributes by creating AssemblyInfo.cs. You can do this in the properties of the Visual Studio project.
[assembly: AssemblyVersion("1.0.0.2")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: AssemblyInformationalVersion("1.0")]

// TODO: Uncomment the following line if the script requires write access.
[assembly: ESAPIScript(IsWriteable = true)]

namespace AutoPlanProstate
{
    class Program
    {
        private static string _patientId;

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                using (Application app = Application.CreateApplication())
                {
                    if (args.Count() > 0)
                    {
                        _patientId = args.First();
                    }
                    Execute(app);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
            }
            Console.ReadLine(); // parses the errors to a window so you can read them
        }
        static void Execute(Application app)
        {
            // TODO: Add your code here.
            if (string.IsNullOrEmpty(_patientId))
            {
                Console.WriteLine("Please input patient Id:");
                _patientId = Console.ReadLine();
            }
            Patient patient = app.OpenPatientById(_patientId);
            patient.BeginModifications(); // Must have this to modify the patient
            Console.WriteLine($"Open patient: {patient.Name}");
            Course course = patient.AddCourse();
            // course.Id = "AutoCourse"; // if you try to name a course the same as ann existing id the code crashes
            var plan = course.AddExternalPlanSetup(patient.StructureSets.FirstOrDefault(x=>x.Id == "CT_1" && x.Image.Id == "CT_2"));
            Console.WriteLine($"Plan generate {plan.Id} on course {course.Id}");
            double[] gantryAngles = new double[] { 230, 290, 320, 40, 70, 130, 180 };

            ExternalBeamMachineParameters parameters = new ExternalBeamMachineParameters("HESN5", "10X", 600, "STATIC", null); // null is equivalent to "" or String.Empty
            foreach(var gantry in gantryAngles)
            {
                plan.AddStaticBeam(parameters,
                    new VRect<double>(-50, -50, 50, 50),
                    0,
                    gantry,
                    0,
                    plan.StructureSet.Image.UserOrigin); // target center of mass, or plan.StructureSet.Image.UserOrigin - new VVector(0, 0, 50)
            }
            Console.WriteLine($"Created {plan.Beams.Count()} fields.");
            plan.SetPrescription(30, new DoseValue(180, DoseValue.DoseUnit.cGy), 1.0);
            Console.WriteLine($"Rx set to {plan.TotalDose} in {plan.NumberOfFractions}fx");
            // get target
            var target = plan.StructureSet.Structures.FirstOrDefault(x => x.Id == "PTVprost SV marg");
            // the calculation property is v16.1 specific
            int i = 0;
            foreach(var rpmodel in app.Calculation.GetDvhEstimationModelSummaries())
            {
                Console.WriteLine($"[{i}].{rpmodel.Name} - {rpmodel.Description}");
                i++;
            }
            Console.WriteLine("Please select a RapidPlan model:");
            var rp_num = Convert.ToInt32(Console.ReadLine());
            var rp = app.Calculation.GetDvhEstimationModelSummaries().ElementAt(rp_num);
            Dictionary<string, DoseValue> targetMatches = new Dictionary<string, DoseValue>();
            Dictionary<string, string> structureMatches = new Dictionary<string, string>();
            foreach(var s in app.Calculation.GetDvhEstimationModelStructures(rp.ModelUID))
            {
                if(s.StructureType == DVHEstimationStructureType.PTV)
                {
                    structureMatches.Add(target.Id, s.Id);
                    targetMatches.Add(target.Id, plan.TotalDose);
                }
                else
                {
                    var structure = plan.StructureSet.Structures.FirstOrDefault(x => x.Id == s.Id);
                    if(structure != null)
                    {
                        structureMatches.Add(structure.Id, s.Id);
                    }
                }
            }
            Console.WriteLine($"Matched structures {String.Join(", ",structureMatches.Select(x=>x.Key))}");
            Console.WriteLine("Calculating DVH Estimates");
            plan.CalculateDVHEstimates(rp.Name, targetMatches, structureMatches);
            Console.WriteLine("Optimizing");
            plan.Optimize();
            Console.WriteLine("Calculating Dose");
            // plan.CalculateDose();
            plan.CalculateLeafMotionsAndDose();
            Console.WriteLine("Saving...");
            app.SaveModifications();
            Console.WriteLine("Plan Saved.");
        }
    }
}
