using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DotVVM.Framework.Compilation.Parser.Binding.Tokenizer;
using DotVVM.Framework.Compilation.Parser.Dothtml.Parser;
using System.Reflection;
using System.Diagnostics;

namespace DotVVM.Framework.Compilation.Parser.Binding.Parser
{
    public class BindingParser : ParserBase<BindingToken, BindingTokenType>
    {
        protected override bool IsWhiteSpace(BindingToken t) => t.Type == BindingTokenType.WhiteSpace;

        public BindingParserNode ReadDirectiveValue()
        {
            var startIndex = CurrentIndex;
            var first = ReadNamespaceOrTypeName();

            if (Peek() != null)
            {
                var @operator = Peek().Type;
                if (@operator == BindingTokenType.AssignOperator)
                {
                    Read();
                    var second = ReadNamespaceOrTypeName();

                    if (first is SimpleNameBindingParserNode)
                    {
                        return CreateNode(new BinaryOperatorBindingParserNode(first, second, @operator), startIndex);
                    }
                    else
                    {
                        first.NodeErrors.Add("Only simple name is allowed as alias.");
                    }
                }
                else
                {
                    first.NodeErrors.Add($"Unexpected operator: {@operator}, expecting assigment (=).");
                }
            }
            return first;
        }

        public BindingParserNode ReadDirectiveTypeName()
        {
            var startIndex = CurrentIndex;
            var typeName = ReadNamespaceOrTypeName();
            if (Peek()?.Type == BindingTokenType.Comma)
            {
                Read();
                var assemblyName = ReadNamespaceOrTypeName();
                if (!(assemblyName is SimpleNameBindingParserNode)) typeName.NodeErrors.Add($"Generic identifier name is not allowed in assembly name.");
                return new AssemblyQualifiedNameBindingParserNode(typeName, assemblyName);
            }
            else if (Peek() != null)
            {
                typeName.NodeErrors.Add($"Unexpected operator: {Peek().Type}, expecting `,` or end.");
            }
            return typeName;
        }

        public BindingParserNode ReadNamespaceOrTypeName()
        {
            return ReadIdentifierExpression(true);
        }

        public BindingParserNode ReadMultiExpression()
        {
            var startIndex = CurrentIndex;
            var expressions = new List<BindingParserNode>();
            expressions.Add(ReadExpression());

            int lastIndex = -1;

            while (!OnEnd())
            {
                if (lastIndex == CurrentIndex)
                {
                    var extraToken = Read();
                    expressions.Add(CreateNode(new LiteralExpressionBindingParserNode(extraToken.Text), lastIndex, "Unexpected token"));
                }

                lastIndex = CurrentIndex;
                var extraNode = ReadExpression();
                extraNode.NodeErrors.Add("Operator expected before this expression.");
                expressions.Add(extraNode);
            }

            return CreateNode(new MultiExpressionBindingParserNode(expressions), startIndex, OnEnd() ? null : $"Unexpected token: {Peek().Text}");
        }

        public BindingParserNode ReadExpression()
        {
            var startIndex = CurrentIndex;
            SkipWhiteSpace();
            return CreateNode(ReadUnsupportedOperatorExpression(), startIndex);
        }

        public bool OnEnd()
        {
            return CurrentIndex >= Tokens.Count;
        }

        private BindingParserNode ReadUnsupportedOperatorExpression()
        {
            var startIndex = CurrentIndex;
            var first = ReadAssignmentExpression();
            if (Peek() != null && Peek().Type == BindingTokenType.UnsupportedOperator)
            {
                var operatorToken = Read();
                var second = ReadUnsupportedOperatorExpression();
                first = CreateNode(new BinaryOperatorBindingParserNode(first, second, BindingTokenType.UnsupportedOperator), startIndex, $"Unsupported operator: {operatorToken.Text}");
            }
            return first;
        }

        private BindingParserNode ReadAssignmentExpression()
        {
            var startIndex = CurrentIndex;
            var first = ReadConditionalExpression();
            if (Peek() != null && Peek().Type == BindingTokenType.AssignOperator)
            {
                Read();
                var second = ReadAssignmentExpression();
                return CreateNode(new BinaryOperatorBindingParserNode(first, second, BindingTokenType.AssignOperator), startIndex);
            }
            else return first;
        }

        private BindingParserNode ReadConditionalExpression()
        {
            var startIndex = CurrentIndex;
            var first = ReadNullCoalescingExpression();
            if (Peek() != null && Peek().Type == BindingTokenType.QuestionMarkOperator)
            {
                Read();
                var second = ReadConditionalExpression();
                var error = IsCurrentTokenIncorrect(BindingTokenType.ColonOperator);
                Read();
                var third = ReadConditionalExpression();

                return CreateNode(new ConditionalExpressionBindingParserNode(first, second, third), startIndex, error ? "The ':' was expected." : null);
            }
            else
            {
                return first;
            }
        }

        private BindingParserNode ReadNullCoalescingExpression()
        {
            var startIndex = CurrentIndex;
            var first = ReadOrElseExpression();
            while (Peek() != null && Peek().Type == BindingTokenType.NullCoalescingOperator)
            {
                Read();
                var second = ReadOrElseExpression();
                first = CreateNode(new BinaryOperatorBindingParserNode(first, second, BindingTokenType.NullCoalescingOperator), startIndex);
            }
            return first;
        }

        private BindingParserNode ReadOrElseExpression()
        {
            var startIndex = CurrentIndex;
            var first = ReadAndAlsoExpression();
            while (Peek() != null && Peek().Type == BindingTokenType.OrElseOperator)
            {
                Read();
                var second = ReadAndAlsoExpression();
                first = CreateNode(new BinaryOperatorBindingParserNode(first, second, BindingTokenType.OrElseOperator), startIndex);
            }
            return first;
        }

        private BindingParserNode ReadAndAlsoExpression()
        {
            var startIndex = CurrentIndex;
            var first = ReadOrExpression();
            while (Peek() != null && Peek().Type == BindingTokenType.AndAlsoOperator)
            {
                Read();
                var second = ReadOrElseExpression();
                first = CreateNode(new BinaryOperatorBindingParserNode(first, second, BindingTokenType.AndAlsoOperator), startIndex);
            }
            return first;
        }

        private BindingParserNode ReadOrExpression()
        {
            var startIndex = CurrentIndex;
            var first = ReadAndExpression();
            while (Peek() != null && Peek().Type == BindingTokenType.OrOperator)
            {
                Read();
                var second = ReadAndExpression();
                first = CreateNode(new BinaryOperatorBindingParserNode(first, second, BindingTokenType.OrOperator), startIndex);
            }
            return first;
        }

        private BindingParserNode ReadAndExpression()
        {
            var startIndex = CurrentIndex;
            var first = ReadEqualityExpression();
            while (Peek() != null && Peek().Type == BindingTokenType.AndOperator)
            {
                Read();
                var second = ReadEqualityExpression();
                first = CreateNode(new BinaryOperatorBindingParserNode(first, second, BindingTokenType.AndOperator), startIndex);
            }
            return first;
        }

        private BindingParserNode ReadEqualityExpression()
        {
            var startIndex = CurrentIndex;
            var first = ReadComparisonExpression();
            while (Peek() != null)
            {
                var @operator = Peek().Type;
                if (@operator == BindingTokenType.EqualsEqualsOperator || @operator == BindingTokenType.NotEqualsOperator)
                {
                    Read();
                    var second = ReadComparisonExpression();
                    first = CreateNode(new BinaryOperatorBindingParserNode(first, second, @operator), startIndex);
                }
                else break;
            }
            return first;
        }

        private BindingParserNode ReadComparisonExpression()
        {
            var startIndex = CurrentIndex;
            var first = ReadAdditiveExpression();
            while (Peek() != null)
            {
                var @operator = Peek().Type;
                if (@operator == BindingTokenType.LessThanEqualsOperator || @operator == BindingTokenType.LessThanOperator
                    || @operator == BindingTokenType.GreaterThanEqualsOperator || @operator == BindingTokenType.GreaterThanOperator)
                {
                    Read();
                    var second = ReadAdditiveExpression();
                    first = CreateNode(new BinaryOperatorBindingParserNode(first, second, @operator), startIndex);
                }
                else break;
            }
            return first;
        }

        private BindingParserNode ReadAdditiveExpression()
        {
            var startIndex = CurrentIndex;
            var first = ReadMultiplicativeExpression();
            while (Peek() != null)
            {
                var @operator = Peek().Type;
                if (@operator == BindingTokenType.AddOperator || @operator == BindingTokenType.SubtractOperator)
                {

                    Read();
                    var second = ReadMultiplicativeExpression();
                    first = CreateNode(new BinaryOperatorBindingParserNode(first, second, @operator), startIndex);
                }
                else break;
            }
            return first;
        }

        private BindingParserNode ReadMultiplicativeExpression()
        {
            var startIndex = CurrentIndex;
            var first = ReadUnaryExpression();
            while (Peek() != null)
            {
                var @operator = Peek().Type;
                if (@operator == BindingTokenType.MultiplyOperator || @operator == BindingTokenType.DivideOperator || @operator == BindingTokenType.ModulusOperator)
                {
                    Read();
                    var second = ReadUnaryExpression();
                    first = CreateNode(new BinaryOperatorBindingParserNode(first, second, @operator), startIndex);
                }
                else break;
            }
            return first;
        }

        private BindingParserNode ReadUnaryExpression()
        {
            var startIndex = CurrentIndex;
            SkipWhiteSpace();

            if (Peek() != null)
            {
                var @operator = Peek().Type;
                var isOperatorUnsupported = @operator == BindingTokenType.UnsupportedOperator;

                if (@operator == BindingTokenType.NotOperator || @operator == BindingTokenType.SubtractOperator || isOperatorUnsupported)
                {
                    var operatorToken = Read();
                    var target = ReadUnaryExpression();
                    return CreateNode(new UnaryOperatorBindingParserNode(target, @operator), startIndex, isOperatorUnsupported ? $"Usupported operator {operatorToken.Text}" : null);
                }
            }
            return CreateNode(ReadIdentifierExpression(false), startIndex);
        }

        private BindingParserNode ReadIdentifierExpression(bool onlyTypeName)
        {
            var startIndex = CurrentIndex;
            BindingParserNode expression = onlyTypeName ? ReadIdentifierNameExpression() : ReadAtomicExpression();


            var next = Peek();
            int previousIndex = -1;
            while (next != null && previousIndex != CurrentIndex)
            {
                previousIndex = CurrentIndex;
                if (next.Type == BindingTokenType.Dot)
                {
                    // member access
                    Read();
                    var member = ReadIdentifierNameExpression();
                    expression = CreateNode(new MemberAccessBindingParserNode(expression, member), startIndex);
                }
                else if (!onlyTypeName && next.Type == BindingTokenType.OpenParenthesis)
                {
                    expression = ReadFunctionCall(startIndex, expression);
                }
                else if (!onlyTypeName && next.Type == BindingTokenType.OpenArrayBrace)
                {
                    expression = ReadArrayAccess(startIndex, expression);
                }
                else
                {
                    break;
                }
                next = Peek();
            }
            return expression;
        }

        private BindingParserNode ReadArrayAccess(int startIndex, BindingParserNode expression)
        {
            // array access
            Read();
            var innerExpression = ReadExpression();
            var error = IsCurrentTokenIncorrect(BindingTokenType.CloseArrayBrace);
            Read();
            SkipWhiteSpace();
            expression = CreateNode(new ArrayAccessBindingParserNode(expression, innerExpression), startIndex, error ? "The ']' was expected." : null);
            return expression;
        }

        private BindingParserNode ReadFunctionCall(int startIndex, BindingParserNode expression)
        {
            // function call
            Read();
            var arguments = new List<BindingParserNode>();
            int previousInnerIndex = -1;
            while (Peek() != null && Peek().Type != BindingTokenType.CloseParenthesis && previousInnerIndex != CurrentIndex)
            {
                previousInnerIndex = CurrentIndex;
                if (arguments.Count > 0)
                {
                    SkipWhiteSpace();
                    if (IsCurrentTokenIncorrect(BindingTokenType.Comma))
                        arguments.Add(CreateNode(new LiteralExpressionBindingParserNode(null), CurrentIndex, "The ',' was expected"));
                    else Read();
                }
                arguments.Add(ReadExpression());
            }
            var error = IsCurrentTokenIncorrect(BindingTokenType.CloseParenthesis);
            Read();
            SkipWhiteSpace();
            expression = CreateNode(new FunctionCallBindingParserNode(expression, arguments), startIndex, error ? "The ')' was expected." : null);
            return expression;
        }

        private BindingParserNode ReadAtomicExpression()
        {
            var startIndex = CurrentIndex;
            SkipWhiteSpace();

            var token = Peek();
            if (token != null && token.Type == BindingTokenType.OpenParenthesis)
            {
                // parenthesised expression
                Read();
                var innerExpression = ReadExpression();
                var error = IsCurrentTokenIncorrect(BindingTokenType.CloseParenthesis);
                Read();
                SkipWhiteSpace();
                return CreateNode(new ParenthesizedExpressionBindingParserNode(innerExpression), startIndex, error ? "The ')' was expected." : null);
            }
            else if (token != null && token.Type == BindingTokenType.StringLiteralToken)
            {
                // string literal
                var literal = Read();
                SkipWhiteSpace();

                string error;
                var node = CreateNode(new LiteralExpressionBindingParserNode(ParseStringLiteral(literal.Text, out error)), startIndex);
                if (error != null)
                {
                    node.NodeErrors.Add(error);
                }
                return node;
            }
            else
            {
                // identifier
                return CreateNode(ReadConstantExpression(), startIndex);
            }
        }

        private BindingParserNode ReadConstantExpression()
        {
            var startIndex = CurrentIndex;
            SkipWhiteSpace();

            if (Peek() != null && Peek().Type == BindingTokenType.Identifier)
            {
                var identifier = Peek();
                if (identifier.Text == "true" || identifier.Text == "false")
                {
                    Read();
                    SkipWhiteSpace();
                    return CreateNode(new LiteralExpressionBindingParserNode(identifier.Text == "true"), startIndex);
                }
                else if (identifier.Text == "null")
                {
                    Read();
                    SkipWhiteSpace();
                    return CreateNode(new LiteralExpressionBindingParserNode(null), startIndex);
                }
                else if (Char.IsDigit(identifier.Text[0]))
                {
                    // number value
                    string error;
                    var number = ParseNumberLiteral(identifier.Text, out error);

                    Read();
                    SkipWhiteSpace();

                    var node = CreateNode(new LiteralExpressionBindingParserNode(number), startIndex);
                    if (error != null)
                    {
                        node.NodeErrors.Add(error);
                    }
                    return node;
                }
            }

            return CreateNode(ReadIdentifierNameExpression(), startIndex);
        }

        private IdentifierNameBindingParserNode ReadIdentifierNameExpression()
        {
            var startIndex = CurrentIndex;
            SkipWhiteSpace();

            if (Peek() != null && Peek().Type == BindingTokenType.Identifier)
            {
                var identifier = Read();
                SkipWhiteSpace();

                if (Peek()!=null && Peek().Type == BindingTokenType.LessThanOperator)
                {
                    return ReadGenericArguments(startIndex, identifier);
                }

                return CreateNode(new SimpleNameBindingParserNode(identifier), startIndex);
            }

            // create virtual empty identifier expression
            return CreateNode(new SimpleNameBindingParserNode(null) { NodeErrors = { "Identifier name was expected!" } }, startIndex);
        }

        private IdentifierNameBindingParserNode ReadGenericArguments(int startIndex, BindingToken identifier)
        {
            Debug.Assert(Peek() != null && Peek().Type == BindingTokenType.LessThanOperator);
            SetRestorePoint();

            var next = Read();
            bool failure = false;
            var previousIndex = -1;
            var arguments = new List<BindingParserNode>();

            while (true)
            {
                if (previousIndex == CurrentIndex || next == null)
                {
                    failure = true;
                    break;
                }

                previousIndex = CurrentIndex;

                SkipWhiteSpace();
                arguments.Add(ReadIdentifierExpression(true));
                SkipWhiteSpace();

                if (Peek()?.Type != BindingTokenType.Comma) { break; }
                Read();
            }

            failure |= Peek()?.Type != BindingTokenType.GreaterThanOperator;

            if (!failure)
            {
                Read();
                ClearRestorePoint();
                return CreateNode(new GenericNameBindingParserNode(identifier, arguments), startIndex);
            }
            Restore();
            return CreateNode(new SimpleNameBindingParserNode(identifier), startIndex);
        }

        private static object ParseNumberLiteral(string text, out string error)
        {
            text = text.ToLower();
            error = null;
            NumberLiteralSuffix type = NumberLiteralSuffix.None;
            var lastDigit = text[text.Length - 1];

            if (ParseNumberLiteralSuffix(ref text, ref error, lastDigit, ref type)) return null;

            object numberLiteral;
            if (ParseNumberLiteralDoubleFloat(text, ref error, type, out numberLiteral)) return numberLiteral;

            const NumberStyles integerStyle = NumberStyles.AllowLeadingSign;
            // try parse integral constant
            object result = null;
            if (type == NumberLiteralSuffix.None)
            {
                result = TryParse<int>(int.TryParse, text, integerStyle) ??
                    TryParse<uint>(uint.TryParse, text, integerStyle) ??
                    TryParse<long>(long.TryParse, text, integerStyle) ??
                    TryParse<ulong>(ulong.TryParse, text, integerStyle);
            }
            else if (type == NumberLiteralSuffix.Unsigned)
            {
                result = TryParse<uint>(uint.TryParse, text, integerStyle) ??
                    TryParse<ulong>(ulong.TryParse, text, integerStyle);
            }
            else if (type == NumberLiteralSuffix.Long)
            {
                result = TryParse<long>(long.TryParse, text, integerStyle) ??
                    TryParse<ulong>(ulong.TryParse, text, integerStyle);
            }
            else if (type == NumberLiteralSuffix.UnsignedLong)
            {
                result = TryParse<ulong>(ulong.TryParse, text, integerStyle);
            }
            if (result != null) return result;
            // handle errors

            // if all are digits, or '0x' + hex digits => too large number
            if (text.All(char.IsDigit) ||
                (text.StartsWith("0x", StringComparison.Ordinal) && text.Skip(2).All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f'))))
                error = $"number number {text} is too large for integral literal, try to append 'd' to real number literal";
            else error = $"could not parse {text} as numeric literal";
            return null;
        }

        private static bool ParseNumberLiteralDoubleFloat(string text, ref string error, NumberLiteralSuffix type,
            out object numberLiteral)
        {
            numberLiteral = null;
            if (text.Contains(".") || text.Contains("e") || type == NumberLiteralSuffix.Float ||
                type == NumberLiteralSuffix.Double)
            {
                const NumberStyles decimalStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
                // real number
                switch (type)
                {
                    case NumberLiteralSuffix.None: // double is defualt
                    case NumberLiteralSuffix.Double:
                        {
                            numberLiteral = TryParse<double>(double.TryParse, text, out error, decimalStyle);
                            return true;
                        }

                    case NumberLiteralSuffix.Float:
                        {
                            numberLiteral = TryParse<float>(float.TryParse, text, out error, decimalStyle);
                            return true;
                        }

                    case NumberLiteralSuffix.Decimal:
                        {
                            numberLiteral = TryParse<decimal>(decimal.TryParse, text, out error, decimalStyle);
                            return true;
                        }

                    default:
                        error = $"could not parse real number of type {type}";
                        {
                            return true;
                        }
                }
            }
            return false;
        }

        private static bool ParseNumberLiteralSuffix(ref string text, ref string error, char lastDigit, ref NumberLiteralSuffix type)
        {
            if (char.IsLetter(lastDigit))
            {
                // number type suffix
                if (lastDigit == 'm') type = NumberLiteralSuffix.Decimal;
                else if (lastDigit == 'f') type = NumberLiteralSuffix.Float;
                else if (lastDigit == 'd') type = NumberLiteralSuffix.Double;
                else if (text.EndsWith("ul", StringComparison.Ordinal) || text.EndsWith("lu", StringComparison.Ordinal))
                    type = NumberLiteralSuffix.UnsignedLong;
                else if (lastDigit == 'u') type = NumberLiteralSuffix.Unsigned;
                else if (lastDigit == 'l') type = NumberLiteralSuffix.Long;
                else
                {
                    error = "number literal type suffix not known";
                    return true;
                }

                if (type == NumberLiteralSuffix.UnsignedLong) text = text.Remove(text.Length - 2); // remove 2 last chars
                else text = text.Remove(text.Length - 1); // remove last char
            }
            return false;
        }

        private delegate bool TryParseDelegate<T>(string text, NumberStyles styles, IFormatProvider format, out T result);

        private static object TryParse<T>(TryParseDelegate<T> method, string text, out string error, NumberStyles styles)
        {
            error = null;
            T result;
            if (method(text, styles, CultureInfo.InvariantCulture, out result)) return result;
            error = $"could not parse { text } using { method.GetMethodInfo().DeclaringType.FullName + "." + method.GetMethodInfo().Name }";
            return null;
        }

        private static object TryParse<T>(TryParseDelegate<T> method, string text, NumberStyles styles)
        {
            T result;
            if (method(text, styles, CultureInfo.InvariantCulture, out result)) return result;
            return null;
        }

        private static string ParseStringLiteral(string text, out string error)
        {
            error = null;
            var sb = new StringBuilder();
            for (var i = 1; i < text.Length - 1; i++)
            {
                if (text[i] == '\\')
                {
                    // handle escaped characters
                    i++;
                    if (i == text.Length - 1)
                    {
                        error = "The escape character cannot be at the end of the string literal!";
                    }
                    else if (text[i] == '\'' || text[i] == '"' || text[i] == '\\')
                    {
                        sb.Append(text[i]);
                    }
                    else if (text[i] == 'n')
                    {
                        sb.Append('\n');
                    }
                    else if (text[i] == 'r')
                    {
                        sb.Append('\r');
                    }
                    else if (text[i] == 't')
                    {
                        sb.Append('\t');
                    }
                    else
                    {
                        error = "The escape sequence is either not valid or not supported in dotVVM bindings!";
                    }
                }
                else
                {
                    sb.Append(text[i]);
                }
            }
            return sb.ToString();
        }

        private T CreateNode<T>(T node, int startIndex, string error = null) where T : BindingParserNode
        {
            node.Tokens.Clear();
            node.Tokens.AddRange(GetTokensFrom(startIndex));

            if (startIndex < Tokens.Count)
            {
                node.StartPosition = Tokens[startIndex].StartPosition;
            }
            else if (startIndex == Tokens.Count && Tokens.Count > 0)
            {
                node.StartPosition = Tokens[startIndex - 1].EndPosition;
            }
            node.Length = node.Tokens.Sum(t => (int?)t.Length) ?? 0;

            if (error != null)
            {
                node.NodeErrors.Add(error);
            }

            return node;
        }

        /// <summary>
        /// Asserts that the current token is of a specified type.
        /// </summary>
        protected bool IsCurrentTokenIncorrect(BindingTokenType desiredType)
        {
            if (Peek() == null || !Peek().Type.Equals(desiredType))
            {
                return true;
            }
            return false;
        }
    }
}