using System.Linq;
using NUnit.Framework;
using OpenTap.Plugins.BasicSteps;

namespace OpenTap.UnitTests
{
    public class AnnotationTest
    {
        [Test]
        public void TestPlanReferenceNameTest()
        {
            var step = new DelayStep();
            var testPlanReference = new TestPlanReference();
            var repeatStep = new RepeatStep();
            repeatStep.ChildTestSteps.Add(testPlanReference);
            var plan = new TestPlan();
            plan.ChildTestSteps.Add(step);
            plan.ChildTestSteps.Add(repeatStep);

            var mem = AnnotationCollection.Annotate(repeatStep).Get<IMembersAnnotation>().Members
                .FirstOrDefault(x => x.Get<IMemberAnnotation>().Member.Name == nameof(RepeatStep.TargetStep));
            var avail = mem.Get<IAvailableValuesAnnotationProxy>().AvailableValues;
            var availStrings = avail.Select(x => x.Get<IStringReadOnlyValueAnnotation>().Value).ToArray();    
            Assert.IsTrue(availStrings.Contains(step.GetFormattedName()));
            Assert.IsTrue(availStrings.Contains(testPlanReference.GetFormattedName()));
        }

        [Test]
        public void SweepLoopEnabledTest()
        {
            var plan = new TestPlan();
            var sweep = new SweepLoop();
            var prog = new ProcessStep();
            plan.ChildTestSteps.Add(sweep);
            sweep.ChildTestSteps.Add(prog);
            
            sweep.SweepParameters.Add(new SweepParam(new [] {TypeData.FromType(prog.GetType()).GetMember(nameof(ProcessStep.RegularExpressionPattern))}));

            var a = AnnotationCollection.Annotate(sweep);
            a.Read();
            var a2 = a.GetMember(nameof(SweepLoop.SweepParameters));
            var col = a2.Get<ICollectionAnnotation>();
            var new1 = col.NewElement();
            col.AnnotatedElements = col.AnnotatedElements.Append(col.NewElement(), new1);
            
            var enabledmem = new1.Get<IMembersAnnotation>().Members.Last();
            var boolmember = enabledmem.Get<IMembersAnnotation>().Members.First();
            var val = boolmember.Get<IObjectValueAnnotation>();
            val.Value = true;
            
            a.Write();

            var sweepParam =  sweep.SweepParameters.FirstOrDefault();
            var en = (Enabled<string>)sweepParam.Values[1];
            Assert.IsTrue(en.IsEnabled); // from val.Value = true.

        }

        public class ErrorMetadataDutResource : Dut
        {
            [MetaData(true)]
            public double ErrorProperty { get; set; } = -5;

            public ErrorMetadataDutResource()
            {
                this.Rules.Add(() => ErrorProperty > 0, "Error property must be > 0", nameof(ErrorProperty));
            }
        }
        
        [Test]
        public void MetadataErrorAnnotation()
        {
            var p = new MetadataPromptObject() {Resources = new Dut[] {new ErrorMetadataDutResource()}};
                
            var test = AnnotationCollection.Annotate(p);
            var forwarded = test.Get<IForwardedAnnotations>();
            var member = forwarded.Forwarded.FirstOrDefault(x =>
                x.Get<IMemberAnnotation>().Member.Name == nameof(ErrorMetadataDutResource.ErrorProperty));
            
            var error = member.GetAll<IErrorAnnotation>().SelectMany(x => x.Errors).ToArray();
            Assert.AreEqual(1, error.Length);

            void checkValue(string value, int errors)
            {
                member.Get<IStringValueAnnotation>().Value = value;
                test.Write();
                test.Read();

                error = member.GetAll<IErrorAnnotation>().SelectMany(x => x.Errors).ToArray();
                Assert.AreEqual(errors, error.Length);
            }

            for (int i = 0; i < 4; i++)
            {
                checkValue("2.0", 0); // no errors.
                checkValue("-2.0", 1); // validation failed.
                checkValue("invalid", 2); // validation failed + parse error.
            }
        }

        public class ClassWithMacroString
        {
            public MacroString String { get; set; } = new MacroString();
        }

        [Test]
        public void TestClassWithMacroStringMultiSelect()
        {
            var elements = new[] {new ClassWithMacroString(), new ClassWithMacroString()};
            var elems = AnnotationCollection.Annotate(elements);
            var sv = elems.GetMember(nameof(ClassWithMacroString.String)).Get<IStringValueAnnotation>();
            sv.Value = "test";
            elems.Write();
            Assert.IsNotNull(sv.Value);
            // Check that multi-edit works.
            foreach (var elem in elements)
            {
                Assert.AreEqual("test", elem.String.ToString());
            }

            // verify that the same MacroPath instance is not being used.
            var elem2 = AnnotationCollection.Annotate(elements[0]);
            var sv2 = elem2.GetMember(nameof(ClassWithMacroString.String)).Get<IStringValueAnnotation>();
            sv2.Value = "test2";
            elem2.Write();
             
            Assert.AreEqual("test", elements[1].String.ToString());
            Assert.AreEqual("test2", elements[0].String.ToString());
            elems.Read();
            Assert.IsNull(sv.Value);
        }
    }
}