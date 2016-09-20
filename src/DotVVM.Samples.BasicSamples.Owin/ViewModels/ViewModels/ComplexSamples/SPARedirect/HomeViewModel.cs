﻿using DotVVM.Framework.AspNetCore.Hosting;
using DotVVM.Framework.Runtime.Filters;
using DotVVM.Framework.ViewModel;
using System.Threading.Tasks;

namespace DotVVM.Samples.BasicSamples.ViewModels.ComplexSamples.SPARedirect
{
    [Authorize()]
	public class HomeViewModel : DotvvmViewModelBase
	{

        public void SignOut()
        {
            Context.GetAuthentication().SignOut("ApplicationCookie");
            
            Context.RedirectToRoute("ComplexSamples_SPARedirect_home", forceRefresh: true);
        }

	}
}
