using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DotVVM.Framework.Configuration;
using DotVVM.Framework.Controls.Infrastructure;
using DotVVM.Framework.Hosting;
using DotVVM.Framework.ResourceManagement;
using DotVVM.Framework.Runtime;
using DotVVM.Framework.Security;
using DotVVM.Framework.ViewModel;
using DotVVM.Framework.ViewModel.Serialization;
using Moq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Microsoft.Owin.Security.DataProtection;
using Newtonsoft.Json.Linq;

namespace DotVVM.Framework.Tests.Runtime
{
	[TestClass]
	public class DefaultViewModelSerializerTests
	{
		private DotvvmConfiguration configuration;
		private DefaultViewModelSerializer serializer;
		private DotvvmRequestContext context;

		[TestInitialize]
		public void TestInit()
		{
			configuration = DotvvmConfiguration.CreateDefault(services =>
			{
				services.AddSingleton<IDataProtectionProvider>(new DpapiDataProtectionProvider("DotVVM Tests"));
                services.AddTransient<IViewModelProtector, DefaultViewModelProtector>();
                services.AddTransient<ICsrfProtector, DefaultCsrfProtector>();
            });
			configuration.Security.SigningKey = Convert.FromBase64String("Uiq1FXs016lC6QaWIREB7H2P/sn4WrxkvFkqaIKpB27E7RPuMipsORgSgnT+zJmUu8zXNSJ4BdL73JEMRDiF6A1ScRNwGyDxDAVL3nkpNlGrSoLNM1xHnVzSbocLFDrdEiZD2e3uKujguycvWSNxYzjgMjXNsaqvCtMu/qRaEGc=");
			configuration.Security.EncryptionKey = Convert.FromBase64String("jNS9I3ZcxzsUSPYJSwzCOm/DEyKFNlBmDGo9wQ6nxKg=");

            var requestMock = new Mock<IHttpRequest>();
            requestMock.SetupGet(m => m.Url).Returns(new Uri("http://localhost:8628/Sample1"));
            requestMock.SetupGet(m => m.Path).Returns(new DotvvmHttpPathString(new PathString("/Sample1")));
            requestMock.SetupGet(m => m.PathBase).Returns(new DotvvmHttpPathString(new PathString("")));
			requestMock.SetupGet(m => m.Scheme).Returns("http");
            requestMock.SetupGet(m => m.Method).Returns("GET");
            requestMock.SetupGet(m => m.Headers).Returns(new DotvvmHeaderCollection(new HeaderDictionary(new Dictionary<string, string[]>())));

            var contextMock = new Mock<IHttpContext>();
            contextMock.SetupGet(m => m.Request).Returns(requestMock.Object);
            contextMock.SetupGet(m => m.User).Returns(new WindowsPrincipal(WindowsIdentity.GetAnonymous()));


			serializer = configuration.ServiceLocator.GetService<IViewModelSerializer>() as DefaultViewModelSerializer;
            context = new DotvvmRequestContext()
            {
                Configuration = configuration,
                HttpContext = contextMock.Object,
                ResourceManager = new ResourceManager(configuration),
                Presenter = configuration.RouteTable.GetDefaultPresenter(),
                ViewModelSerializer = serializer
            };
        }



		[TestMethod]
		public void DefaultViewModelSerializer_NoEncryptedValues()
		{
			var oldViewModel = new TestViewModel()
			{
				Property1 = "a",
				Property2 = 4,
				Property3 = new DateTime(2000, 1, 1),
				Property4 = new List<TestViewModel2>()
				{
					new TestViewModel2() { PropertyA = "t", PropertyB = 15 },
					new TestViewModel2() { PropertyA = "xxx", PropertyB = 16 }
				},
				Property5 = null
			};
			context.ViewModel = oldViewModel;
			serializer.BuildViewModel(context);
			var result = context.GetSerializedViewModel();
			result = UnwrapSerializedViewModel(result);
			result = WrapSerializedViewModel(result);

			var newViewModel = new TestViewModel();
			context.ViewModel = newViewModel;
			serializer.PopulateViewModel(context, result);

			Assert.AreEqual(oldViewModel.Property1, newViewModel.Property1);
			Assert.AreEqual(oldViewModel.Property2, newViewModel.Property2);
			Assert.AreEqual(oldViewModel.Property3, newViewModel.Property3);
			Assert.AreEqual(oldViewModel.Property4[0].PropertyA, newViewModel.Property4[0].PropertyA);
			Assert.AreEqual(oldViewModel.Property4[0].PropertyB, newViewModel.Property4[0].PropertyB);
			Assert.AreEqual(oldViewModel.Property4[1].PropertyA, newViewModel.Property4[1].PropertyA);
			Assert.AreEqual(oldViewModel.Property4[1].PropertyB, newViewModel.Property4[1].PropertyB);
			Assert.AreEqual(oldViewModel.Property5, newViewModel.Property5);
		}

		[TestMethod]
		public void Serializer_Valid_ExistingValueNotReplaced()
		{
			var json = SerializeViewModel(new TestViewModel12 { Property = new TestViewModel13 { MyProperty = 56 } });

			var viewModel = new TestViewModel12 { Property = new TestViewModel13 { MyProperty = 55 } };
			viewModel.Property.SetPrivateField(123);
			PopulateViewModel(viewModel, json);
			Assert.AreEqual(56, viewModel.Property.MyProperty);
			Assert.AreEqual(123, viewModel.Property.GetPrivateField());
		}

       

        private string SerializeViewModel(object viewModel)
		{
			context.ViewModel = viewModel;
			serializer.SendDiff = false;
			serializer.BuildViewModel(context);
			return UnwrapSerializedViewModel(serializer.SerializeViewModel(context));
		}

		private void PopulateViewModel(object viewModel, string json)
		{
			context.ViewModel = viewModel;
			serializer.PopulateViewModel(context,
				"{'validationTargetPath': null,'viewModel':" + json + "}");
		}

		class TestViewModel12
		{
			public TestViewModel13 Property { get; set; }
		}
		class TestViewModel13
		{
			public int MyProperty { get; set; }
			private int privateField = 33;
			public void SetPrivateField(int value)
			{
				privateField = value;
			}
			public int GetPrivateField() => privateField;
		}


		public class TestViewModel
		{
			public string Property1 { get; set; }
			public int Property2 { get; set; }
			public DateTime Property3 { get; set; }
			public List<TestViewModel2> Property4 { get; set; }
			public string Property5 { get; set; }
		}
		public class TestViewModel2
		{
			public string PropertyA { get; set; }
			public int PropertyB { get; set; }
		}



		[TestMethod]
		public void DefaultViewModelSerializer_SignedAndEncryptedValue()
		{
			var oldViewModel = new TestViewModel3()
			{
				Property1 = "a",
				Property2 = 4,
				Property3 = new DateTime(2000, 1, 1),
				Property4 = new List<TestViewModel4>()
				{
					new TestViewModel4() { PropertyA = "t", PropertyB = 15 },
					new TestViewModel4() { PropertyA = "xxx", PropertyB = 16 }
				}
			};
			context.ViewModel = oldViewModel;

			serializer.BuildViewModel(context);
			var result = context.GetSerializedViewModel();
			result = UnwrapSerializedViewModel(result);
			result = WrapSerializedViewModel(result);

			var newViewModel = new TestViewModel3();
			context.ViewModel = newViewModel;
			serializer.PopulateViewModel(context, result);

			Assert.AreEqual(oldViewModel.Property1, newViewModel.Property1);
			Assert.AreEqual(oldViewModel.Property2, newViewModel.Property2);
			Assert.AreEqual(oldViewModel.Property3, newViewModel.Property3);
			Assert.AreEqual(oldViewModel.Property4[0].PropertyA, newViewModel.Property4[0].PropertyA);
			Assert.AreEqual(oldViewModel.Property4[0].PropertyB, newViewModel.Property4[0].PropertyB);
			Assert.AreEqual(oldViewModel.Property4[1].PropertyA, newViewModel.Property4[1].PropertyA);
			Assert.AreEqual(oldViewModel.Property4[1].PropertyB, newViewModel.Property4[1].PropertyB);
		}



		public class TestViewModel3
		{
			public string Property1 { get; set; }

			[Protect(ProtectMode.SignData)]
			public int Property2 { get; set; }

			[Protect(ProtectMode.EncryptData)]
			public DateTime Property3 { get; set; }
			public List<TestViewModel4> Property4 { get; set; }
		}
		public class TestViewModel4
		{
			[Protect(ProtectMode.SignData)]
			public string PropertyA { get; set; }

			[Protect(ProtectMode.EncryptData)]
			public int PropertyB { get; set; }
		}



		[TestMethod]
		public void DefaultViewModelSerializer_SignedAndEncryptedValue_NullableInt_NullValue()
		{
			var oldViewModel = new TestViewModel5()
			{
				ProtectedNullable = null
			};
			context.ViewModel = oldViewModel;

			serializer.BuildViewModel(context);
			var result = context.GetSerializedViewModel();
			result = UnwrapSerializedViewModel(result);
			result = WrapSerializedViewModel(result);

			var newViewModel = new TestViewModel5();
			context.ViewModel = newViewModel;
			serializer.PopulateViewModel(context, result);

			Assert.AreEqual(oldViewModel.ProtectedNullable, newViewModel.ProtectedNullable);
		}


		class TestViewModel5
		{
			[Protect(ProtectMode.SignData)]
			public int? ProtectedNullable { get; set; }
		}


		[TestMethod]
		public void DefaultViewModelSerializer_Enum()
		{
			var oldViewModel = new EnumTestViewModel()
			{
				Property1 = TestEnum.Second
			};
			context.ViewModel = oldViewModel;
			serializer.BuildViewModel(context);
			var result = context.GetSerializedViewModel();
			result = UnwrapSerializedViewModel(result);
			result = WrapSerializedViewModel(result);

			var newViewModel = new EnumTestViewModel();
			context.ViewModel = newViewModel;
			serializer.PopulateViewModel(context, result);

			Assert.IsFalse(result.Contains(typeof(TestEnum).FullName));
			Assert.AreEqual(oldViewModel.Property1, newViewModel.Property1);
		}


		public class EnumCollectionTestViewModel
		{
			public TestEnum Property1 { get; set; }

			public List<EnumTestViewModel> Children { get; set; }
		}

		public class EnumTestViewModel
		{
			public TestEnum Property1 { get; set; }
		}

		public enum TestEnum
		{
			First,
			Second,
			Third
		}




		[TestMethod]
		public void DefaultViewModelSerializer_EnumInCollection()
		{
			var oldViewModel = new EnumCollectionTestViewModel()
			{
				Property1 = TestEnum.Third,
				Children = new List<EnumTestViewModel>()
				{
					new EnumTestViewModel() { Property1 = TestEnum.First },
					new EnumTestViewModel() { Property1 = TestEnum.Second },
					new EnumTestViewModel() { Property1 = TestEnum.Third }
				}
			};

			context.ViewModel = oldViewModel;
			serializer.BuildViewModel(context);
			var result = context.GetSerializedViewModel();
			result = UnwrapSerializedViewModel(result);
			result = WrapSerializedViewModel(result);

			var newViewModel = new EnumCollectionTestViewModel() { Children = new List<EnumTestViewModel>() };
			context.ViewModel = newViewModel;
			serializer.PopulateViewModel(context, result);

			Assert.IsFalse(result.Contains(typeof(TestEnum).FullName));
			Assert.AreEqual(oldViewModel.Property1, newViewModel.Property1);
			Assert.AreEqual(oldViewModel.Children[0].Property1, newViewModel.Children[0].Property1);
			Assert.AreEqual(oldViewModel.Children[1].Property1, newViewModel.Children[1].Property1);
			Assert.AreEqual(oldViewModel.Children[2].Property1, newViewModel.Children[2].Property1);
		}

        [TestMethod]
        public void Serializer_Valid_BindBothOnGetOnlyProperty()
        {
            var json = SerializeViewModel(new GetOnlyPropertyViewModel { Property = 42 });

            var viewModel = new GetOnlyPropertyViewModel();
            PopulateViewModel(viewModel, json);
            Assert.AreEqual(43, viewModel.PropertyPlusOne);
        }

        public class GetOnlyPropertyViewModel
        {
            public int Property { get; set; }
            [Bind(Direction.Both)]
            public int PropertyPlusOne => Property + 1;
        }


        /// <summary>
        /// Wraps the serialized view model to an object that comes from the client.
        /// </summary>
        private static string WrapSerializedViewModel(string result)
		{
			return string.Format("{{'currentPath':[],'command':'','controlUniqueId':'','viewModel':{0},'validationTargetPath':'','updatedControls':{{}}}}".Replace("'", "\""), result);
		}

		/// <summary>
		/// Unwraps the object that goes to the client to the serialized view model.
		/// </summary>
		private static string UnwrapSerializedViewModel(string result)
		{
			return JObject.Parse(result)["viewModel"].ToString();
		}

	}
}
