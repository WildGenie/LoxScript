﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using XPT.Core.Utilities;

namespace XPT.Core.Scripting.LoxScript.VirtualMachine {
    internal class GearsNativeWrapper {
        private static readonly Dictionary<Type, GearsNativeWrapper> _Wrappers = new Dictionary<Type, GearsNativeWrapper>();

        public static GearsNativeWrapper GetWrapper(Type type) {
            if (_Wrappers.TryGetValue(type, out GearsNativeWrapper wrapper)) {
                return wrapper;
            }
            try {
                wrapper = new GearsNativeWrapper(type, true);
            }
            catch (Exception e) {
                throw new GearsRuntimeException($"GearsNativeWrapper.GetWrapper: Could not wrap object of type {type.Name}. Inner error: {e.Message}");
            }
            _Wrappers[type] = wrapper;
            return wrapper;
        }

        public readonly Type WrappedType;
        private readonly Dictionary<ulong, FieldInfo> _Fields = new Dictionary<ulong, FieldInfo>();
        private readonly Dictionary<ulong, MethodInfo> _Methods = new Dictionary<ulong, MethodInfo>();
        private readonly Dictionary<ulong, PropertyInfo> _Properties = new Dictionary<ulong, PropertyInfo>();

        private GearsNativeWrapper(Type wrappedType, bool wrapAllPublicFields = false) {
            WrappedType = wrappedType;
            BindingFlags binding = BindingFlags.Public | BindingFlags.Instance;
            FieldInfo[] fields = wrappedType.GetFields(binding);
            foreach (FieldInfo info in fields) {
                LoxFieldAttribute attr = info.GetCustomAttribute<LoxFieldAttribute>();
                if (!wrapAllPublicFields && attr == null) {
                    continue;
                }
                ulong name = BitString.GetBitStr(attr?.Name ?? info.Name);
                if (NameExists(name)) {
                    Tracer.Warn($"GearsNativeWrapper: {wrappedType.Name}.{info.Name} is masked by a field, method, or property named '{BitString.GetBitStr(name)}'.");
                    continue;
                }
                _Fields.Add(name, info);
            }
            MethodInfo[] methods = wrappedType.GetMethods(binding).Where(d => !d.IsSpecialName).ToArray();
            foreach (MethodInfo info in methods) {
                LoxFieldAttribute attr = info.GetCustomAttribute<LoxFieldAttribute>();
                if (!wrapAllPublicFields && attr == null) {
                    continue;
                }
                ulong name = BitString.GetBitStr(attr?.Name ?? info.Name);
                if (NameExists(name)) {
                    Tracer.Warn($"GearsNativeWrapper: {wrappedType.Name}.{info.Name} is masked by a field, method, or property named '{BitString.GetBitStr(name)}'.");
                    continue;
                }
                _Methods.Add(name, info);
            }
            PropertyInfo[] properties = wrappedType.GetProperties(binding);
            foreach (PropertyInfo info in properties) {
                LoxFieldAttribute attr = info.GetCustomAttribute<LoxFieldAttribute>();
                if (!wrapAllPublicFields && attr == null) {
                    continue;
                }
                ulong name = BitString.GetBitStr(attr?.Name ?? info.Name);
                if (NameExists(name)) {
                    Tracer.Warn($"GearsNativeWrapper: {wrappedType.Name}.{info.Name} is masked by a field, method, or property named '{BitString.GetBitStr(name)}'.");
                    continue;
                }
                _Properties.Add(name, info);
            }
        }

        private bool NameExists(ulong name) => _Fields.ContainsKey(name) || _Methods.ContainsKey(name) || _Properties.ContainsKey(name);

        public void SetField(Gears context, object wrappedObject, ulong name, GearsValue value) {
            if (_Fields.TryGetValue(name, out FieldInfo fieldInfo)) {
                if (value.IsNumber) {
                    if (!IsNumeric(fieldInfo.FieldType)) {
                        throw new GearsRuntimeException($"Attempted to set {WrappedType.Name}.{fieldInfo.Name} to numeric value.");
                    }
                    try {
                        fieldInfo.SetValue(wrappedObject, Convert.ChangeType((double)value, fieldInfo.FieldType));
                        return;
                    }
                    catch (Exception e) {
                        throw new GearsRuntimeException($"Error setting {WrappedType.Name}.{fieldInfo.Name} to {(double)value}: {e.Message}");
                    }
                }
                else if (value.IsNil && fieldInfo.FieldType == typeof(string)) {
                    fieldInfo.SetValue(wrappedObject, null);
                    return;
                }
                else if (fieldInfo.FieldType == typeof(bool) && value.IsBool) {
                    fieldInfo.SetValue(wrappedObject, value.IsTrue);
                    return;
                }
                else if (value.IsObjPtr) {
                    GearsObj obj = value.AsObject(context);
                    if (fieldInfo.FieldType == typeof(string) && obj is GearsObjString objString) {
                        fieldInfo.SetValue(wrappedObject, objString.Value);
                        return;
                    }
                }
            }
            else if (_Properties.TryGetValue(name, out PropertyInfo propertyInfo)) {
                if (!propertyInfo.GetSetMethod().IsPublic) {
                    throw new GearsRuntimeException($"Unsupported reference: Native class {WrappedType.Name} does not have a public set method for '{BitString.GetBitStr(name)}'.");
                }
                if (value.IsNumber) {
                    if (!IsNumeric(propertyInfo.PropertyType)) {
                        throw new GearsRuntimeException($"Attempted to set {WrappedType.Name}.{propertyInfo.Name} to numeric value.");
                    }
                    try {
                        propertyInfo.SetValue(wrappedObject, Convert.ChangeType((double)value, propertyInfo.PropertyType), null);
                        return;
                    }
                    catch (Exception e) {
                        throw new GearsRuntimeException($"Error setting {WrappedType.Name}.{propertyInfo.Name} to {(double)value}: {e.Message}");
                    }
                }
                else if (value.IsNil && propertyInfo.PropertyType == typeof(string)) {
                    propertyInfo.SetValue(wrappedObject, null, null);
                    return;
                }
                else if (propertyInfo.PropertyType == typeof(bool) && value.IsBool) {
                    propertyInfo.SetValue(wrappedObject, value.IsTrue, null);
                    return;
                }
                else if (value.IsObjPtr) {
                    GearsObj obj = value.AsObject(context);
                    if (propertyInfo.PropertyType == typeof(string) && obj is GearsObjString objString) {
                        propertyInfo.SetValue(wrappedObject, objString.Value, null);
                        return;
                    }
                }
            }
            throw new GearsRuntimeException($"Unsupported native conversion: Error setting {WrappedType.Name}.{BitString.GetBitStr(name)} to {value}.");
        }

        public bool TryGetField(Gears vm, object wrappedObject, ulong name, out GearsValue value) {
            if (_Fields.TryGetValue(name, out FieldInfo fieldInfo)) {
                if (IsNumeric(fieldInfo.FieldType)) {
                    double fieldValue = Convert.ToDouble(fieldInfo.GetValue(wrappedObject));
                    value = new GearsValue(fieldValue);
                    return true;
                }
                else if (fieldInfo.FieldType == typeof(bool)) {
                    bool fieldValue = Convert.ToBoolean(fieldInfo.GetValue(wrappedObject));
                    value = fieldValue ? GearsValue.TrueValue : GearsValue.FalseValue;
                    return true;
                }
                else if (fieldInfo.FieldType == typeof(string)) {
                    if (!(fieldInfo.GetValue(wrappedObject) is string fieldValue)) {
                        value = GearsValue.NilValue;
                    }
                    else {
                        value = GearsValue.CreateObjPtr(vm.HeapAddObject(new GearsObjString(fieldValue)));
                    }
                    return true;
                }
                else if (fieldInfo.FieldType.IsSubclassOf(typeof(object))) {
                    if (!(fieldInfo.GetValue(wrappedObject) is object wrappedFieldObject)) {
                        value = GearsValue.NilValue;
                    }
                    else {
                        value = GearsValue.CreateObjPtr(vm.HeapAddObject(new GearsObjInstanceNative(vm, wrappedFieldObject)));
                    }
                    return true;
                }
            }
            else if (_Methods.TryGetValue(name, out MethodInfo methodInfo)) {
                value = GearsValue.CreateObjPtr(vm.HeapAddObject(
                    new GearsObjFunctionNative(methodInfo.Name, methodInfo.GetParameters().Length, 
                        (GearsValue[] args) => CreateNativeClosure(vm, wrappedObject, methodInfo, args))));
                return true;
            }
            else if (_Properties.TryGetValue(name, out PropertyInfo propertyInfo)) {
                if (!propertyInfo.GetGetMethod().IsPublic) {
                    throw new GearsRuntimeException($"Unsupported reference: Native class {WrappedType.Name} does not have a public get method for '{BitString.GetBitStr(name)}'.");
                }
                if (IsNumeric(propertyInfo.PropertyType)) {
                    double fieldValue = Convert.ToDouble(propertyInfo.GetValue(wrappedObject, null));
                    value = new GearsValue(fieldValue);
                    return true;
                }
                else if (propertyInfo.PropertyType == typeof(bool)) {
                    bool fieldValue = Convert.ToBoolean(propertyInfo.GetValue(wrappedObject, null));
                    value = fieldValue ? GearsValue.TrueValue : GearsValue.FalseValue;
                    return true;
                }
                else if (propertyInfo.PropertyType == typeof(string)) {
                    if (!(propertyInfo.GetValue(wrappedObject, null) is string fieldValue)) {
                        value = GearsValue.NilValue;
                    }
                    else {
                        value = GearsValue.CreateObjPtr(vm.HeapAddObject(new GearsObjString(fieldValue)));
                    }
                    return true;
                }
            }
            throw new GearsRuntimeException($"Unsupported reference: Native class {WrappedType.Name} does not have a public field named '{BitString.GetBitStr(name)}'.");
        }

        private static GearsValue CreateNativeClosure(Gears context, object receiver, MethodInfo methodInfo, GearsValue[] args) {
            object[] parameters = new object[args.Length];
            ParameterInfo[] paramInfo = methodInfo.GetParameters();
            if (paramInfo.Length != parameters.Length) {
                throw new GearsRuntimeException($"NativeWrapper error: {receiver.GetType().Name}.{methodInfo.Name} param info count did not match passed param count.");
            }
            for (int i = 0; i < args.Length; i++) {
                GearsValue value = args[i];
                ParameterInfo info = paramInfo[i];
                if (value.IsNumber) {
                    if (!IsNumeric(info.ParameterType)) {
                        throw new GearsRuntimeException($"Attempted to set {receiver.GetType().Name}.{info.Name} to numeric value.");
                    }
                    try {
                        if (info.ParameterType.IsEnum) {
                            parameters[i] = Enum.ToObject(info.ParameterType, (int)value);
                        }
                        else {
                            parameters[i] = Convert.ChangeType((double)value, info.ParameterType);
                        }
                        continue;
                    }
                    catch (Exception e) {
                        throw new GearsRuntimeException($"Error setting {receiver.GetType().Name}.{info.Name} to {(double)value}: {e.Message}");
                    }
                }
                else if (value.IsNil && info.ParameterType == typeof(string)) {
                    parameters[i] = null;
                    continue;
                }
                else if (value.IsBool && info.ParameterType == typeof(bool)) {
                    parameters[i] = value.IsTrue ? true : false;
                    continue;
                }
                else if (value.IsObjPtr) {
                    GearsObj obj = value.AsObject(context);
                    if (info.ParameterType == typeof(string) && obj is GearsObjString objString) {
                        parameters[i] = objString.Value;
                        continue;
                    }
                    else if (info.ParameterType.IsSubclassOf(typeof(object)) && obj is GearsObjInstanceNative objNative && objNative.WrappedObject.GetType() == info.ParameterType) {
                        parameters[i] = objNative.WrappedObject;
                        continue;
                    }
                }
                throw new GearsRuntimeException($"Unsupported native conversion: Error casting parameter {i} for {receiver.GetType().Name}.{info.Name} to {info.ParameterType.Name}.");
            }
            object returnValue = methodInfo.Invoke(receiver, parameters);
            if (methodInfo.ReturnType == typeof(void)) {
                return GearsValue.NilValue;
            }
            else if (IsNumeric(methodInfo.ReturnType)) {
                return new GearsValue(Convert.ToDouble(returnValue));
            }
            else if (methodInfo.ReturnType == typeof(bool)) {
                return Convert.ToBoolean(returnValue) ? GearsValue.TrueValue : GearsValue.FalseValue;
            }
            else if (methodInfo.ReturnType == typeof(string)) {
                if (returnValue == null) {
                    return GearsValue.NilValue;
                }
                else {
                    return GearsValue.CreateObjPtr(context.HeapAddObject(new GearsObjString(Convert.ToString(returnValue))));
                }
            }
            else {
                throw new GearsRuntimeException($"Unsupported native return type: {receiver.GetType().Name}.{methodInfo.Name} cannot return type of {methodInfo.ReturnType.Name}.");
            }
        }

        internal static bool IsNumeric(Type type) {
            TypeCode code = Type.GetTypeCode(type);
            switch (code) {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }
    }
}
