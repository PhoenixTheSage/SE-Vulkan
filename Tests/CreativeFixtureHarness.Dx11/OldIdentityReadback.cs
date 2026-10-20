using System;
using System.IO;
using System.Xml.Linq;
using FixtureAssets;
using VRageRender;

namespace CreativeFixtureHarness;

static partial class Dx11LightingReadback
{
    public static void VerifyOldPartIdentities(string reports)
    {
        var layouts=new XElement("OldLayoutIdentityOracle",new XAttribute("Scope","Read-only initialized stock layout snapshot and source bootstrap comparison."));int layoutCount=0;
        void LayoutCheck(bool passed,string message){layouts.Add(new XElement("Check",new XAttribute("Passed",passed),message));if(!passed){new XDocument(layouts).Save(Path.Combine(reports,"old-layout-identities.xml"));throw new InvalidOperationException(message);}layoutCount++;}
        StockOldLayoutIdentityOracle.Run(typeof(MyRenderSettings1Extensions).Assembly,layouts,LayoutCheck);layouts.SetAttributeValue("Checks",layoutCount);layouts.SetAttributeValue("Passed",true);new XDocument(layouts).Save(Path.Combine(reports,"old-layout-identities.xml"));
        var result=new XElement("OldPartIdentityOracle",new XAttribute("Scope","Actual initialized stock CPU part builder after held capture; no live table resets."));int count=0;
        void Check(bool passed,string message){result.Add(new XElement("Check",new XAttribute("Passed",passed),message));if(!passed){new XDocument(result).Save(Path.Combine(reports,"old-part-identities.xml"));throw new InvalidOperationException(message);}count++;}
        StockOldPartIdentityOracle.Run(typeof(MyRenderSettings1Extensions).Assembly,Check);result.SetAttributeValue("Checks",count);result.SetAttributeValue("Passed",true);new XDocument(result).Save(Path.Combine(reports,"old-part-identities.xml"));
    }
}
