﻿using System;
using System.Collections.Generic;

namespace dot10.DotNet {
	/// <summary>
	/// Finds <see cref="TypeDef"/>s
	/// </summary>
	sealed class TypeDefFinder : ITypeDefFinder, IDisposable {
		const SigComparerOptions TypeComparerOptions = SigComparerOptions.DontCompareTypeScope;
		bool isCacheEnabled;
		readonly bool includeNestedTypes;
		Dictionary<ITypeDefOrRef, TypeDef> typeRefCache = new Dictionary<ITypeDefOrRef, TypeDef>(new TypeEqualityComparer(TypeComparerOptions));
		Dictionary<string, TypeDef> normalNameCache = new Dictionary<string, TypeDef>(StringComparer.Ordinal);
		Dictionary<string, TypeDef> reflectionNameCache = new Dictionary<string, TypeDef>(StringComparer.Ordinal);
		IEnumerator<TypeDef> typeEnumerator;
		readonly IEnumerable<TypeDef> rootTypes;

		/// <summary>
		/// <c>true</c> if the <see cref="TypeDef"/> cache is enabled. <c>false</c> if the cache
		/// is disabled and a slower <c>O(n)</c> lookup is performed.
		/// </summary>
		public bool IsCacheEnabled {
			get { return isCacheEnabled; }
			set {
				if (isCacheEnabled == value)
					return;

				if (typeEnumerator != null) {
					typeEnumerator.Dispose();
					typeEnumerator = null;
				}

				typeRefCache.Clear();
				normalNameCache.Clear();
				reflectionNameCache.Clear();

				if (value)
					InitializeTypeEnumerator();

				isCacheEnabled = value;
			}
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="rootTypes">All root types. All their nested types are also included.</param>
		public TypeDefFinder(IEnumerable<TypeDef> rootTypes)
			: this(rootTypes, true) {
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="rootTypes">All root types</param>
		/// <param name="includeNestedTypes"><c>true</c> if all nested types that are reachable
		/// from <paramref name="rootTypes"/> should also be included.</param>
		/// <exception cref="ArgumentNullException">If <paramref name="rootTypes"/> is <c>null</c></exception>
		public TypeDefFinder(IEnumerable<TypeDef> rootTypes, bool includeNestedTypes) {
			if (rootTypes == null)
				throw new ArgumentNullException("rootTypes");
			this.rootTypes = rootTypes;
			this.includeNestedTypes = includeNestedTypes;
		}

		void InitializeTypeEnumerator() {
			if (typeEnumerator != null) {
				typeEnumerator.Dispose();
				typeEnumerator = null;
			}
			typeEnumerator = GetTypeDefsToIterateOver(rootTypes, includeNestedTypes).GetEnumerator();
		}

		static IEnumerable<TypeDef> GetTypeDefsToIterateOver(IEnumerable<TypeDef> types, bool includeNestedTypes) {
			if (!includeNestedTypes)
				return types;
			return GetTypesAndNestedTypesToIterateOver(types, 0);
		}

		static IEnumerable<TypeDef> GetTypesAndNestedTypesToIterateOver(IEnumerable<TypeDef> types, int recursionLevel) {
			if (types != null && recursionLevel < 100) {
				foreach (var type in types) {
					yield return type;
					if (type.NestedTypes.Count > 0) {
						foreach (var nested in GetTypesAndNestedTypesToIterateOver(type.NestedTypes, recursionLevel + 1))
							yield return nested;
					}
				}
			}
		}

		/// <inheritdoc/>
		public TypeDef Find(string fullName, bool isReflectionName) {
			if (fullName == null)
				return null;
			if (isCacheEnabled)
				return isReflectionName ? FindCacheReflection(fullName) : FindCacheNormal(fullName);
			return isReflectionName ? FindSlowReflection(fullName) : FindSlowNormal(fullName);
		}

		/// <inheritdoc/>
		public TypeDef Find(TypeRef typeRef) {
			if (typeRef == null)
				return null;
			return isCacheEnabled ? FindCache(typeRef) : FindSlow(typeRef);
		}

		TypeDef FindCache(TypeRef typeRef) {
			TypeDef cachedType;
			if (typeRefCache.TryGetValue(typeRef, out cachedType))
				return cachedType;

			// Build the cache lazily
			var comparer = new SigComparer { Options = TypeComparerOptions };
			while (true) {
				cachedType = GetNextTypeDefCache();
				if (cachedType == null || comparer.Equals(cachedType, typeRef))
					return cachedType;
			}
		}

		TypeDef FindCacheReflection(string fullName) {
			TypeDef cachedType;
			if (reflectionNameCache.TryGetValue(fullName, out cachedType))
				return cachedType;

			// Build the cache lazily
			var comparer = new SigComparer { Options = TypeComparerOptions };
			while (true) {
				cachedType = GetNextTypeDefCache();
				if (cachedType == null || cachedType.ReflectionFullName == fullName)
					return cachedType;
			}
		}

		TypeDef FindCacheNormal(string fullName) {
			TypeDef cachedType;
			if (normalNameCache.TryGetValue(fullName, out cachedType))
				return cachedType;

			// Build the cache lazily
			var comparer = new SigComparer { Options = TypeComparerOptions };
			while (true) {
				cachedType = GetNextTypeDefCache();
				if (cachedType == null || cachedType.FullName == fullName)
					return cachedType;
			}
		}

		TypeDef FindSlow(TypeRef typeRef) {
			InitializeTypeEnumerator();
			var comparer = new SigComparer { Options = TypeComparerOptions };
			while (true) {
				var type = GetNextTypeDef();
				if (type == null || comparer.Equals(type, typeRef))
					return type;
			}
		}

		TypeDef FindSlowReflection(string fullName) {
			InitializeTypeEnumerator();
			while (true) {
				var type = GetNextTypeDef();
				if (type == null || type.ReflectionFullName == fullName)
					return type;
			}
		}

		TypeDef FindSlowNormal(string fullName) {
			InitializeTypeEnumerator();
			while (true) {
				var type = GetNextTypeDef();
				if (type == null || type.FullName == fullName)
					return type;
			}
		}

		/// <summary>
		/// Gets the next <see cref="TypeDef"/> or <c>null</c> if there are no more left
		/// </summary>
		/// <returns>The next <see cref="TypeDef"/> or <c>null</c> if none</returns>
		TypeDef GetNextTypeDef() {
			while (typeEnumerator.MoveNext()) {
				var type = typeEnumerator.Current;
				if (type != null)
					return type;
			}
			return null;
		}

		/// <summary>
		/// Gets the next <see cref="TypeDef"/> or <c>null</c> if there are no more left.
		/// The cache is updated with the returned <see cref="TypeDef"/> before the method
		/// returns.
		/// </summary>
		/// <returns>The next <see cref="TypeDef"/> or <c>null</c> if none</returns>
		TypeDef GetNextTypeDefCache() {
			var type = GetNextTypeDef();
			if (type == null)
				return null;

			// Only insert it if another type with the exact same sig/name isn't already
			// in the cache. This should only happen with some obfuscated assemblies.

			if (!typeRefCache.ContainsKey(type))
				typeRefCache[type] = type;
			string fn;
			if (!normalNameCache.ContainsKey(fn = type.FullName))
				normalNameCache[fn] = type;
			if (!reflectionNameCache.ContainsKey(fn = type.ReflectionFullName))
				reflectionNameCache[fn] = type;

			return type;
		}

		/// <inheritdoc/>
		public void Dispose() {
			if (typeEnumerator != null)
				typeEnumerator.Dispose();
			typeRefCache = null;
			normalNameCache = null;
			reflectionNameCache = null;
		}
	}
}
