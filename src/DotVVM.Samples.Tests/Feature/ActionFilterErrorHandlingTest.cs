﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dotvvm.Samples.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Riganti.Utils.Testing.Selenium.Core;

namespace DotVVM.Samples.Tests.Feature
{
    [TestClass]
    public class ActionFilterErrorHandlingTest : SeleniumTestBase
    {
        [TestMethod]
        public void Feature_ActionFilterErrorHandling_CommandException()
        {
            RunInAllBrowsers(browser =>
            {
                browser.NavigateToUrl(SamplesRouteUrls.FeatureSamples_ActionFilterErrorHandling_ActionFilterErrorHandling);

                browser.Single(".result").CheckIfInnerTextEquals("no error");

                browser.ElementAt("input[type=button]", 0).Click();
                browser.Single("iframe").CheckIfIsNotDisplayed();
                browser.Single(".result").CheckIfInnerTextEquals("error was handled");

                browser.ElementAt("input[type=button]", 1).Click();
                browser.Single("iframe").CheckIfIsDisplayed();
            });
        }

        [TestMethod]
        public void Feature_ActionFilterErrorHandling_PageException()
        {
            RunInAllBrowsers(browser =>
            {
                browser.NavigateToUrl(SamplesRouteUrls.FeatureSamples_ActionFilterErrorHandling_ActionFilterPageErrorHandling);
                browser.Wait(1000);
                browser.CheckUrl(u => u.Contains("error500"));
            });
        }
    }
}
