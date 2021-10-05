//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core.Utils;

namespace Microsoft.AppMagic.Authoring.Texl
{
    internal static class ArgumentSuggestions
    {
        internal delegate IEnumerable<KeyValuePair<string, DType>> GetArgumentSuggestionsDelegate(TryGetEnumSymbol tryGetEnumSymbol, DType scopeType, int argumentIndex, out bool requiresSuggestionEscaping);
        private delegate IEnumerable<KeyValuePair<string, DType>> GetArgumentSuggestionsDelegateWithoutEnum(DType scopeType, int argumentIndex, out bool requiresSuggestionEscaping);

        internal delegate bool TryGetEnumSymbol(string symbolName, out EnumSymbol symbol);

        internal static readonly Lazy<Dictionary<Type, GetArgumentSuggestionsDelegate>> CustomFunctionSuggestionProviders =
            new Lazy<Dictionary<Type, GetArgumentSuggestionsDelegate>>(() => new Dictionary<Type, GetArgumentSuggestionsDelegate>
            {
                { typeof(DateDiffFunction), TimeUnitSuggestions },
                { typeof(DateAddFunction), TimeUnitSuggestions },
                { typeof(DateValueFunction), LanguageCodeSuggestion },
                { typeof(TimeValueFunction), LanguageCodeSuggestion },
                { typeof(DateTimeValueFunction), LanguageCodeSuggestion },
                { typeof(IfFunction), IfSuggestions },
                { typeof(EndsWithFunction), DiscardEnumParam(StringTypeSuggestions) },
                { typeof(SplitFunction), DiscardEnumParam(StringTypeSuggestions) },
                { typeof(StartsWithFunction), DiscardEnumParam(StringTypeSuggestions) },
                { typeof(TextFunction), TextSuggestions  },
                { typeof(ValueFunction), LanguageCodeSuggestion },
            }, isThreadSafe: true);

        public static IEnumerable<KeyValuePair<string, DType>> GetArgumentSuggestions(TryGetEnumSymbol tryGetEnumSymbol, TexlFunction function, DType scopeType, int argumentIndex, out bool requiresSuggestionEscaping)
        {
            if (CustomFunctionSuggestionProviders.Value.TryGetValue(function.GetType(), out var suggestor))
                return suggestor(tryGetEnumSymbol, scopeType, argumentIndex, out requiresSuggestionEscaping);

            requiresSuggestionEscaping = false;
            return Enumerable.Empty<KeyValuePair<string, DType>>();
        }

        public static void TestOnly_AddFunctionHandler(TexlFunction func, GetArgumentSuggestionsDelegate suggestor)
        {
            CustomFunctionSuggestionProviders.Value.Add(func.GetType(), suggestor);
        }

        private static GetArgumentSuggestionsDelegate DiscardEnumParam(GetArgumentSuggestionsDelegateWithoutEnum suggestor)
        {
            return (TryGetEnumSymbol tryGetEnumSymbol, DType scopeType, int argumentIndex, out bool requiresSuggestionEscaping) =>
                suggestor(scopeType, argumentIndex, out requiresSuggestionEscaping);
        }

        /// <summary>
        /// This method returns the suggestions for second and third arguments of the Text function.
        /// </summary>
        /// <param name="tryGetEnumSymbol">
        /// Getter for enum symbols intended for the suggestions
        /// </param>
        /// <param name="scopeType">
        /// Type of the enclosing scope from where intellisense is run
        /// </param>
        /// <param name="argumentIndex">
        /// The current index of the argument from where intellisense is run
        /// </param>
        /// <param name="requiresSuggestionEscaping">
        /// Set to whether the argument needs to be string escaped
        /// </param>
        /// <returns>
        /// Enumerable of suggestions wherein the key is the suggestion text and the value is its type
        /// </returns>
        private static IEnumerable<KeyValuePair<string, DType>> TextSuggestions(TryGetEnumSymbol tryGetEnumSymbol, DType scopeType, int argumentIndex, out bool requiresSuggestionEscaping)
        {
            Contracts.Assert(scopeType.IsValid);
            Contracts.Assert(0 <= argumentIndex);

            requiresSuggestionEscaping = true;

            if (argumentIndex != 1 && argumentIndex != 2)
                return EnumerableUtils.Yield<KeyValuePair<string, DType>>();

            if (argumentIndex == 1)
            {
                if (!DType.DateTime.Accepts(scopeType) || !tryGetEnumSymbol(EnumConstants.DateTimeFormatEnumString, out var enumInfo))
                    return EnumerableUtils.Yield<KeyValuePair<string, DType>>();

                var retVal = new List<KeyValuePair<string, DType>>();
                Contracts.AssertValue(enumInfo);

                requiresSuggestionEscaping = false;
                foreach (var name in enumInfo.EnumType.GetNames(DPath.Root))
                {
                    string locName;
                    enumInfo.TryGetLocValueName(name.Name.Value, out locName).Verify();
                    retVal.Add(new KeyValuePair<string, DType>(TexlLexer.EscapeName(enumInfo.Name) + TexlLexer.PunctuatorDot + TexlLexer.EscapeName(locName), name.Type));
                }

                return retVal;
            }
            else
            {
                Contracts.Assert(argumentIndex == 2);

                requiresSuggestionEscaping = false;
                return GetLanguageCodeSuggestions();
            }
        }

        /// <summary>
        /// Cached list of language code suggestions
        /// </summary>
        private static IEnumerable<KeyValuePair<string, DType>> _languageCodeSuggestions;

        /// <summary>
        /// Initializes or retrieves from the cache <see cref="_languageCodeSuggestions"/>
        /// </summary>
        /// <returns>
        /// List of language code suggestions
        /// </returns>
        internal static IEnumerable<KeyValuePair<string, DType>> GetLanguageCodeSuggestions()
        {
            if (_languageCodeSuggestions == null)
            {
                Interlocked.CompareExchange(
                    ref _languageCodeSuggestions,
                    TexlStrings.SupportedDateTimeLanguageCodes(null).Split(new[] { ',' }).Select(locale => new KeyValuePair<string, DType>(locale, DType.String)),
                    null);
            }

            return _languageCodeSuggestions;
        }

        private static IEnumerable<KeyValuePair<string, DType>> StringTypeSuggestions(DType scopeType, int argumentIndex, out bool requiresSuggestionEscaping)
        {
            Contracts.AssertValid(scopeType);
            Contracts.Assert(0 <= argumentIndex);

            requiresSuggestionEscaping = true;

            if (argumentIndex == 0)
                return IntellisenseHelper.GetSuggestionsFromType(scopeType, DType.String);

            return EnumerableUtils.Yield<KeyValuePair<string, DType>>();
        }

        private static IEnumerable<KeyValuePair<string, DType>> TimeUnitSuggestions(TryGetEnumSymbol tryGetEnumSymbol, DType scopeType, int argumentIndex, out bool requiresSuggestionEscaping)
        {
            Contracts.Assert(scopeType.IsValid);
            Contracts.Assert(2 == argumentIndex);

            requiresSuggestionEscaping = true;
            var retVal = new List<KeyValuePair<string, DType>>();

            if (argumentIndex == 2 && tryGetEnumSymbol(EnumConstants.TimeUnitEnumString, out var enumInfo))
            {
                Contracts.AssertValue(enumInfo);
                foreach (var name in enumInfo.EnumType.GetNames(DPath.Root))
                {
                    string locName;
                    enumInfo.TryGetLocValueName(name.Name.Value, out locName).Verify();
                    retVal.Add(new KeyValuePair<string, DType>(TexlLexer.EscapeName(locName), name.Type));
                }
            }

            return retVal;
        }

        private static IEnumerable<KeyValuePair<string, DType>> LanguageCodeSuggestion(TryGetEnumSymbol tryGetEnumSymbol, DType scopeType, int argumentIndex, out bool requiresSuggestionEscaping)
        {
            Contracts.Assert(scopeType.IsValid);
            Contracts.Assert(0 <= argumentIndex);

            requiresSuggestionEscaping = false;
            return argumentIndex == 1 ? GetLanguageCodeSuggestions() : EnumerableUtils.Yield<KeyValuePair<string, DType>>();
        }

        // This method returns the suggestions for latter arguments of the If function based on the second argument (the true result)
        private static IEnumerable<KeyValuePair<string, DType>> IfSuggestions(TryGetEnumSymbol tryGetEnumSymbol, DType scopeType, int argumentIndex, out bool requiresSuggestionEscaping)
        {
            Contracts.Assert(scopeType.IsValid);
            Contracts.Assert(0 <= argumentIndex);

            requiresSuggestionEscaping = false;

            if (argumentIndex <= 1)
                return EnumerableUtils.Yield<KeyValuePair<string, DType>>();

            return scopeType
                .GetNames(DPath.Root)
                .Select(name => new KeyValuePair<string, DType>(TexlLexer.EscapeName(name.Name.Value), name.Type));
        }
    }
}