﻿using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Hl7.Fhir.Introspection
{
    public class PropertyMapping
    {
        public string Name { get; private set; }

        private ICollection<ChoiceAttribute> _choices = null;

        public bool HasChoices
        {
            get { return _choices != null; }
        }

        public bool IsCollection { get; private set; }

        public bool IsPrimitive { get; private set; }
        public bool RepresentsValueElement { get; private set; }

        public Type ReturnType { get; private set; }
        public Type ElementType { get; private set; }

        public static PropertyMapping Create(PropertyInfo prop)
        {
            IEnumerable<Type> dummy;

            return Create(prop, out dummy);
        }

        
        internal static PropertyMapping Create(PropertyInfo prop, out IEnumerable<Type> referredTypes)        
        {
            if (prop == null) throw Error.ArgumentNull("prop");

            var foundTypes = new List<Type>();

            PropertyMapping result = new PropertyMapping();
            result.Name = getMappedElementName(prop);
            result.ReturnType = prop.PropertyType;
            result.ElementType = result.ReturnType;

            foundTypes.Add(result.ElementType);

            result.IsCollection = ReflectionHelper.IsTypedCollection(prop.PropertyType) && !prop.PropertyType.IsArray;

            // Get to the actual (native) type representing this element
            if (result.IsCollection) result.ElementType = ReflectionHelper.GetCollectionItemType(prop.PropertyType);
            if (ReflectionHelper.IsNullableType(result.ElementType)) result.ElementType = ReflectionHelper.GetNullableArgument(result.ElementType);
            result.IsPrimitive = isAllowedNativeTypeForDataTypeValue(result.ElementType);

            // Check wether this property represents a native .NET type
            // marked to receive the class' primitive value in the fhir serialization
            // (e.g. the value from the Xml 'value' attribute or the Json primitive member value)
            if(result.IsPrimitive) result.RepresentsValueElement = isPrimitiveValueElement(prop);

            // Get the choice attributes. If there are none, set the _choices to null instead
            // of an empty list, which saves checking using Count() while parsing.
            result._choices = ReflectionHelper.GetAttributes<ChoiceAttribute>(prop);
            if (result._choices != null && result._choices.Count == 0) result._choices = null;

            if (result.HasChoices)
                foundTypes.AddRange(result._choices.Select(cattr => cattr.Type));

            referredTypes = foundTypes;

            // May need to generate getters/setters using pre-compiled expression trees for performance.
            // See http://weblogs.asp.net/marianor/archive/2009/04/10/using-expression-trees-to-get-property-getter-and-setters.aspx
            result._getter = instance => prop.GetValue(instance, null);
            result._setter = (instance,value) => prop.SetValue(instance, value, null);
            
            return result;
        }


        private static string buildQualifiedPropName(PropertyInfo prop)
        {
            return prop.DeclaringType.Name + "." + prop.Name;
        }


        private static bool isPrimitiveValueElement(PropertyInfo prop)
        {
            var valueElementAttr = (FhirElementAttribute)Attribute.GetCustomAttribute(prop, typeof(FhirElementAttribute));
            var isValueElement = valueElementAttr != null && valueElementAttr.IsPrimitiveValue;

            if(isValueElement && !isAllowedNativeTypeForDataTypeValue(prop.PropertyType))
                throw Error.Argument("prop", "Property {0} is marked for use as a primitive element value, but its .NET type ({1}) is not supported by the serializer.", buildQualifiedPropName(prop), prop.PropertyType.Name);

            return isValueElement;
        }


         //// Special case: this is a member that uses the closed generic Code<T> type - 
         //       // do mapping for its open, defining type instead
         //       if (elementType.IsGenericType)
         //       {
         //           if (ReflectionHelper.IsClosedGenericType(elementType) &&  
         //               ReflectionHelper.IsConstructedFromGenericTypeDefinition(elementType, typeof(Code<>)) )
         //           {
         //               result.CodeOfTEnumType = elementType.GetGenericArguments()[0];
         //               elementType = elementType.GetGenericTypeDefinition();
         //           }
         //           else
         //               throw Error.NotSupported("Property {0} on type {1} uses an open generic type, which is not yet supported", prop.Name, prop.DeclaringType.Name);
         //       }

        public bool MatchesSuffixedName(string suffixedName)
        {
            if (suffixedName == null) throw Error.ArgumentNull("suffixedName");

            return this.HasChoices && suffixedName.ToUpperInvariant().StartsWith(Name.ToUpperInvariant());
        }

        public string GetChoiceSuffixFromName(string suffixedName)
        {
            if (suffixedName == null) throw Error.ArgumentNull("suffixedName");

            if (MatchesSuffixedName(suffixedName))
                return suffixedName.Remove(0, Name.Length);
            else
                throw Error.Argument("suffixedName", "The given suffixed name {0} does not match this property's name {1}",
                                            suffixedName, Name);
        }

        public bool IsAllowedChoice(string choiceSuffix)
        {
            var suffix = choiceSuffix.ToUpperInvariant();
            return HasChoices && _choices.Any(cattr => cattr.TypeName.ToUpperInvariant() == suffix);
        }

        public Type GetChoiceType(string choiceSuffix)
        {
            string suffix = choiceSuffix.ToUpperInvariant();

            if(!HasChoices) return null;

            return _choices
                        .Where(cattr => cattr.TypeName.ToUpperInvariant() == suffix)
                        .Select(cattr => cattr.Type)
                        .FirstOrDefault(); 
        }

        public bool HasAnyResourceWildcard()
        {
            if (!HasChoices) return false;

            return _choices.Any(ca => ca.Wildcard == WildcardChoice.AnyResource);
        }

        public bool HasAnyDataTypeWildcard()
        {
            if (!HasChoices) return false;

            return _choices.Any(ca => ca.Wildcard == WildcardChoice.AnyDatatype);
        }


        private static string getMappedElementName(PropertyInfo prop)
        {
            var attr = (FhirElementAttribute)Attribute.GetCustomAttribute(prop, typeof(FhirElementAttribute));

            if (attr != null)
                return attr.Name;
            else
                return prop.Name;
        }

        private static bool isAllowedNativeTypeForDataTypeValue(Type type)
        {
            // Special case, allow Nullable<enum>
            if (ReflectionHelper.IsNullableType(type))
                type = ReflectionHelper.GetNullableArgument(type);

            return type.IsEnum ||
                    PrimitiveTypeConverter.CanConvert(type);
        }


        private Func<object, object> _getter;
        private Action<object, object> _setter;

        public object GetValue(object instance)
        {
            return _getter(instance);
        }

        public void SetValue(object instance, object value)
        {
            _setter(instance, value);
        }
    }
}