using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DotVVM.Framework.Binding;
using DotVVM.Framework.Compilation.Parser.Dothtml.Parser;
using DotVVM.Framework.Runtime;
using DotVVM.Framework.Compilation.Parser.Binding.Parser;
using DotVVM.Framework.Compilation.Binding;
using DotVVM.Framework.Utils;

namespace DotVVM.Framework.Compilation.ControlTree.Resolved
{
    public class ResolvedTreeBuilder : IAbstractTreeBuilder
    {

        public IAbstractTreeRoot BuildTreeRoot(IControlTreeResolver controlTreeResolver, IControlResolverMetadata metadata, DothtmlRootNode node, IDataContextStack dataContext, IReadOnlyDictionary<string, IReadOnlyList<IAbstractDirective>> directives)
        {
            return new ResolvedTreeRoot((ControlResolverMetadata)metadata, node, (DataContextStack)dataContext, directives);
        }

        public IAbstractControl BuildControl(IControlResolverMetadata metadata, DothtmlNode node, IDataContextStack dataContext)
        {
            return new ResolvedControl((ControlResolverMetadata)metadata, node, (DataContextStack)dataContext);
        }

        public IAbstractBinding BuildBinding(BindingParserOptions bindingOptions, IDataContextStack dataContext, DothtmlBindingNode node, ITypeDescriptor resultType = null, Exception parsingError = null, object customData = null)
        {
            return new ResolvedBinding()
            {
                BindingType = bindingOptions.BindingType,
                Value = node.Value,
                Expression = (Expression)customData,
                DataContextTypeStack = (DataContextStack)dataContext,
                ParsingError = parsingError,
                DothtmlNode = node,
                ResultType = resultType
            };
        }

        public IAbstractPropertyBinding BuildPropertyBinding(IPropertyDescriptor property, IAbstractBinding binding, DothtmlAttributeNode sourceAttribute)
        {
            return new ResolvedPropertyBinding((DotvvmProperty)property, (ResolvedBinding)binding) { DothtmlNode = sourceAttribute };
        }

        public IAbstractPropertyControl BuildPropertyControl(IPropertyDescriptor property, IAbstractControl control, DothtmlElementNode wrapperElement)
        {
            return new ResolvedPropertyControl((DotvvmProperty)property, (ResolvedControl)control) { DothtmlNode = wrapperElement };
        }

        public IAbstractPropertyControlCollection BuildPropertyControlCollection(IPropertyDescriptor property, IEnumerable<IAbstractControl> controls, DothtmlElementNode wrapperElement)
        {
            return new ResolvedPropertyControlCollection((DotvvmProperty)property, controls.Cast<ResolvedControl>().ToList()) { DothtmlNode = wrapperElement };
        }

        public IAbstractPropertyTemplate BuildPropertyTemplate(IPropertyDescriptor property, IEnumerable<IAbstractControl> templateControls, DothtmlElementNode wrapperElement)
        {
            return new ResolvedPropertyTemplate((DotvvmProperty)property, templateControls.Cast<ResolvedControl>().ToList()) { DothtmlNode = wrapperElement };
        }

        public IAbstractPropertyValue BuildPropertyValue(IPropertyDescriptor property, object value, DothtmlNode sourceNode)
        {
            return new ResolvedPropertyValue((DotvvmProperty)property, value) { DothtmlNode = sourceNode };
        }

        public IAbstractImportDirective BuildImportDirective(
            DothtmlDirectiveNode node,
            BindingParserNode aliasSyntax,
            BindingParserNode nameSyntax)
        {
            foreach (var syntaxNode in nameSyntax.EnumerateNodes().Concat(aliasSyntax?.EnumerateNodes() ?? Enumerable.Empty<BindingParserNode>()))
            {
                syntaxNode.NodeErrors.ForEach(node.AddError);
            }

            var expression = ParseDirectiveExpression(node, nameSyntax);

            if (expression is UnknownStaticClassIdentifierExpression)
            {
                var namespaceValid = expression
                    .CastTo<UnknownStaticClassIdentifierExpression>().Name
                    .Apply(ReflectionUtils.IsAssemblyNamespace);

                if (!namespaceValid)
                {
                    node.AddError($"{nameSyntax.ToDisplayString()} is unknown type or namespace.");
                }

                return new ResolvedImportDirective(aliasSyntax, nameSyntax, null) { DothtmlNode = node };

            }
            else if (expression is StaticClassIdentifierExpression)
            {
                return new ResolvedImportDirective(aliasSyntax, nameSyntax, expression.Type) { DothtmlNode = node };
            }

            node.AddError($"{nameSyntax.ToDisplayString()} is not a type or namespace.");
            return new ResolvedImportDirective(aliasSyntax, nameSyntax, null) { DothtmlNode = node };
        }

        public IAbstractViewModelDirective BuildViewModelDirective(DothtmlDirectiveNode directive, BindingParserNode nameSyntax)
        {
            var type = ResolveTypeNameDirective(directive, nameSyntax);
            return new ResolvedViewModelDirective(nameSyntax, type) { DothtmlNode = directive };
        }

        public IAbstractBaseTypeDirective BuildBaseTypeDirective(DothtmlDirectiveNode directive, BindingParserNode nameSyntax)
        {
            var type = ResolveTypeNameDirective(directive, nameSyntax);
            return new ResolvedBaseTypeDirective(nameSyntax, type) { DothtmlNode = directive };
        }

        static ResolvedTypeDescriptor ResolveTypeNameDirective(DothtmlDirectiveNode directive, BindingParserNode nameSyntax)
        {
            var expression = ParseDirectiveExpression(directive, nameSyntax) as StaticClassIdentifierExpression;
            if (expression == null)
            {
                directive.AddError($"Could not resolve type '{nameSyntax.ToDisplayString()}'.");
                return null;
            }
            else return new ResolvedTypeDescriptor(expression.Type);
        }

        static Expression ParseDirectiveExpression(DothtmlDirectiveNode directive, BindingParserNode expressionSyntax)
        {
            TypeRegistry registry;
            if (expressionSyntax is AssemblyQualifiedNameBindingParserNode)
            {
                var assemblyQualifiedName = expressionSyntax as AssemblyQualifiedNameBindingParserNode;
                expressionSyntax = assemblyQualifiedName.TypeName;
                registry = TypeRegistry.DirectivesDefault(assemblyQualifiedName.AssemblyName.ToDisplayString());
            }
            else
            {
                registry = TypeRegistry.DirectivesDefault();
            }

            var visitor = new ExpressionBuildingVisitor(registry)
            {
                ResolveOnlyTypeName = true,
                Scope = null
            };

            try
            {
                return visitor.Visit(expressionSyntax);
            }
            catch (Exception ex)
            {
                directive.AddError($"{expressionSyntax.ToDisplayString()} is not a valid type or namespace: {ex.Message}");
                return null;
            }
        }

        public IAbstractDirective BuildDirective(DothtmlDirectiveNode node)
        {
            return new ResolvedDirective() { DothtmlNode = node };
        }

        public bool AddProperty(IAbstractControl control, IAbstractPropertySetter setter, out string error)
        {
            return ((ResolvedControl)control).SetProperty((ResolvedPropertySetter)setter, false, out error);
        }

        public void AddChildControl(IAbstractContentNode control, IAbstractControl child)
        {
            ((ResolvedContentNode)control).AddChild((ResolvedControl)child);
        }
    }
}