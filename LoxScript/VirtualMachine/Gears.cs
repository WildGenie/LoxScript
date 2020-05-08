﻿using System;
using static LoxScript.VirtualMachine.EGearsOpCode;

namespace LoxScript.VirtualMachine {
    /// <summary>
    /// Gears is a bytecode virtual machine for the Lox language.
    /// </summary>
    partial class Gears {

        private const string InitString = "init";

        private GearsValue NativeFnClock(GearsValue[] args) {
            return new GearsValue((double)DateTimeOffset.Now.ToUnixTimeMilliseconds());
        }

        internal bool Run(GearsObjFunction script) {
            Reset(script);
            DefineNative("clock", 0, NativeFnClock);
            while (true) {
                EGearsOpCode instruction = (EGearsOpCode)ReadByte();
                switch (instruction) {
                    case OP_CONSTANT:
                        Push(ReadConstant());
                        break;
                    case OP_STRING:
                        Push(GearsValue.CreateObjPtr(HeapAddObject(new GearsObjString(ReadConstantString()))));
                        break;
                    case OP_FUNCTION: {
                            int arity = ReadByte();
                            string name = ReadConstantString();
                            Push(GearsValue.CreateObjPtr(HeapAddObject(new GearsObjFunction(this, name, arity))));
                        }
                        break;
                    case OP_NIL:
                        Push(GearsValue.NilValue);
                        break;
                    case OP_TRUE:
                        Push(GearsValue.TrueValue);
                        break;
                    case OP_FALSE:
                        Push(GearsValue.FalseValue);
                        break;
                    case OP_POP:
                        Pop();
                        break;
                    case OP_GET_LOCAL: {
                            int slot = ReadShort();
                            Push(StackGet(slot + _BP));
                        }
                        break;
                    case OP_SET_LOCAL: {
                            int slot = ReadShort();
                            StackSet(slot + _BP, Peek());
                        }
                        break;
                    case OP_GET_GLOBAL: {
                            string name = ReadConstantString();
                            if (!Globals.TryGet(name, out GearsValue value)) {
                                throw new GearsRuntimeException(0, $"Undefined variable '{name}'.");
                            }
                            Push(value);
                        }
                        break;
                    case OP_DEFINE_GLOBAL: {
                            string name = ReadConstantString();
                            Globals.Set(name, Peek());
                            Pop();
                        }
                        break;
                    case OP_SET_GLOBAL: {
                            string name = ReadConstantString();
                            if (!Globals.ContainsKey(name)) {
                                throw new GearsRuntimeException(0, $"Undefined variable '{name}'.");
                            }
                            Globals.Set(name, Peek());
                            break;
                        }
                    case OP_GET_UPVALUE: {
                            int slot = ReadShort();
                            GearsObjUpvalue upvalue = (_OpenFrame as GearsCallFrameClosure).Closure.Upvalues[slot];
                            if (upvalue.IsClosed) {
                                Push(upvalue.Value);
                            }
                            else {
                                Push(StackGet(upvalue.OriginalSP));
                            }
                        }
                        break;
                    case OP_SET_UPVALUE: {
                            int slot = ReadShort();
                            GearsObjUpvalue upvalue = (_OpenFrame as GearsCallFrameClosure).Closure.Upvalues[slot];
                            if (upvalue.IsClosed) {
                                upvalue.Value = Peek();
                            }
                            else {
                                StackSet(upvalue.OriginalSP, Peek());
                            }
                        }
                        break;
                    case OP_GET_PROPERTY: {
                            GearsObjInstance instance = GetObjectFromPtr<GearsObjInstance>(Peek());
                            string name = ReadConstantString(); // property name.
                            if (instance.Fields.TryGet(name, out GearsValue value)) {
                                Pop(); // instance
                                Push(value); // property value
                                break;
                            }
                            if (!BindMethod(instance.Class, name)) {
                                throw new GearsRuntimeException(0, $"Undefined property or method '{name}'.");
                            }
                        }
                        break;
                    case OP_SET_PROPERTY: {
                            GearsObjInstance instance = GetObjectFromPtr<GearsObjInstance>(Peek(1));
                            string name = ReadConstantString(); // property name.
                            GearsValue value = Pop(); // value
                            instance.Fields.Set(name, value);
                            Pop(); // ptr
                            Push(value); // value
                        }
                        break;
                    case OP_GET_SUPER: {
                            string name = ReadConstantString(); // property name.
                            GearsObjClass superclass = GetObjectFromPtr<GearsObjClass>(Pop());
                            if (!BindMethod(superclass, name)) {
                                throw new GearsRuntimeException(0, $"Could not get method {name} in superclass {superclass}");
                            }
                        }
                        break;
                    case OP_EQUAL:
                        Push(AreValuesEqual(Pop(), Pop()));
                        break;
                    case OP_GREATER: {
                            if (!Peek(0).IsNumber || !Peek(1).IsNumber) {
                                throw new GearsRuntimeException(0, "Operands must be numbers.");
                            }
                            GearsValue b = Pop();
                            GearsValue a = Pop();
                            Push(a > b);
                        }
                        break;
                    case OP_LESS: {
                            if (!Peek(0).IsNumber || !Peek(1).IsNumber) {
                                throw new GearsRuntimeException(0, "Operands must be numbers.");
                            }
                            GearsValue b = Pop();
                            GearsValue a = Pop();
                            Push(a < b);
                        }
                        break;
                    case OP_ADD: {
                            if (Peek(0).IsNumber && Peek(1).IsNumber) {
                                GearsValue b = Pop();
                                GearsValue a = Pop();
                                Push(a + b);
                            }
                            else if (Peek(0).IsObjType(this, GearsObj.ObjType.ObjString) && Peek(1).IsObjType(this, GearsObj.ObjType.ObjString)) {
                                string b = GetObjectFromPtr<GearsObjString>(Pop()).Value;
                                string a = GetObjectFromPtr<GearsObjString>(Pop()).Value;
                                Push(GearsValue.CreateObjPtr(HeapAddObject(new GearsObjString(a + b))));
                            }
                            else {
                                throw new GearsRuntimeException(0, "Operands must be numbers or strings.");
                            }
                        }
                        break;
                    case OP_SUBTRACT: {
                            if (!Peek(0).IsNumber || !Peek(1).IsNumber) {
                                throw new GearsRuntimeException(0, "Operands must be numbers.");
                            }
                            GearsValue b = Pop();
                            GearsValue a = Pop();
                            Push(a - b);
                        }
                        break;
                    case OP_MULTIPLY: {
                            if (!Peek(0).IsNumber || !Peek(1).IsNumber) {
                                throw new GearsRuntimeException(0, "Operands must be numbers.");
                            }
                            GearsValue b = Pop();
                            GearsValue a = Pop();
                            Push(a * b);
                        }
                        break;
                    case OP_DIVIDE: {
                            if (!Peek(0).IsNumber || !Peek(1).IsNumber) {
                                throw new GearsRuntimeException(0, "Operands must be numbers.");
                            }
                            GearsValue b = Pop();
                            GearsValue a = Pop();
                            Push(a / b);
                        }
                        break;
                    case OP_NOT:
                        Push(IsFalsey(Pop()));
                        break;
                    case OP_NEGATE: {
                            if (!Peek(0).IsNumber) {
                                throw new GearsRuntimeException(0, "Operand must be a number.");
                            }
                            Push(-Pop());
                        }
                        break;
                    case OP_PRINT:
                        Console.WriteLine(Pop().ToString(this));
                        break;
                    case OP_JUMP: {
                            int offset = ReadShort();
                            ModIP(offset);
                        }
                        break;
                    case OP_JUMP_IF_FALSE: {
                            int offset = ReadShort();
                            if (IsFalsey(Peek())) {
                                ModIP(offset);
                            }
                        }
                        break;
                    case OP_LOOP: {
                            int offset = ReadShort();
                            ModIP(-offset);
                        }
                        break;
                    case OP_CALL: {
                            Call();
                        }
                        break;
                    case OP_INVOKE: {
                            CallInvoke();
                        }
                        break;
                    case OP_SUPER_INVOKE: {
                            CallInvokeSuper();
                        }
                        break;
                    case OP_CLOSURE: {
                            GearsValue ptr = Pop();
                            if (!ptr.IsObjPtr) {
                                throw new GearsRuntimeException(0, "Attempted closure of non-pointer.");
                            }
                            GearsObj obj = HeapGetObject(ptr.AsObjPtr);
                            if (obj.Type == GearsObj.ObjType.ObjFunction) {
                                int upvalueCount = ReadByte();
                                GearsObjClosure closure = new GearsObjClosure(obj as GearsObjFunction, upvalueCount);
                                for (int i = 0; i < upvalueCount; i++) {
                                    bool isLocal = ReadByte() == 1;
                                    int index = ReadByte();
                                    if (isLocal) {
                                        int location = _OpenFrame.BP + index;
                                        closure.Upvalues[i] = CaptureUpvalue(location);
                                    }
                                    else {
                                        closure.Upvalues[i] = (_OpenFrame as GearsCallFrameClosure).Closure.Upvalues[index];
                                    }
                                }
                                Push(GearsValue.CreateObjPtr(HeapAddObject(closure)));
                                break;
                            }
                        }
                        throw new GearsRuntimeException(0, "Can only make closures from functions.");
                    case OP_CLOSE_UPVALUE:
                        CloseUpvalues(_SP - 1);
                        Pop();
                        break;
                    case OP_RETURN: {
                            GearsValue result = Pop();
                            CloseUpvalues(_OpenFrame.BP);
                            if (PopFrame()) {
                                if (_SP != 0) {
                                    Console.WriteLine($"Report error: SP is '{_SP}', not '0'.");
                                }
                                return true;
                            }
                            Push(result);
                        }
                        break;
                    case OP_CLASS: {
                            Push(GearsValue.CreateObjPtr(HeapAddObject(new GearsObjClass(ReadConstantString()))));
                        }
                        break;
                    case OP_INHERIT: {
                            if (!Peek(0).IsObjType(this, GearsObj.ObjType.ObjClass)) {
                                throw new GearsRuntimeException(0, "Superclass is not a class.");
                            }
                            GearsObjClass super = GetObjectFromPtr<GearsObjClass>(Peek(1));
                            GearsObjClass sub = GetObjectFromPtr<GearsObjClass>(Peek(0));
                            foreach (string key in super.Methods.AllKeys) {
                                if (!super.Methods.TryGet(key, out GearsValue methodPtr)) {
                                    throw new GearsRuntimeException(0, "Could not copy superclass method table.");
                                }
                                sub.Methods.Set(key, methodPtr);
                            }
                            Pop(); // pop subclass
                        }
                        break;
                    case OP_METHOD: {
                            DefineMethod();
                        }
                        break;
                    default:
                        throw new GearsRuntimeException(0, $"Unknown opcode {instruction}");
                }
            }
        }

        private GearsValue AreValuesEqual(GearsValue a, GearsValue b) {
            if (a.IsBool && b.IsBool) {
                return a.AsBool == b.AsBool;
            }
            else if (a.IsNil && b.IsNil) {
                return true;
            }
            else if (a.IsNumber && b.IsNumber) {
                return a.Equals(b);
            }
            return false;
        }

        /// <summary>
        /// Lox has a simple rule for boolean values: 'false' and 'nil' are falsey, and everything else is truthy.
        /// </summary>
        private bool IsFalsey(GearsValue value) {
            return value.IsNil || (value.IsBool && !value.AsBool);
        }

        private T GetObjectFromPtr<T>(GearsValue ptr) where T : GearsObj {
            if (!ptr.IsObjPtr) {
                throw new Exception($"GetObjectFromPtr: Value is not a pointer and cannot reference a {typeof(T).Name}.");
            }
            GearsObj obj = HeapGetObject(ptr.AsObjPtr);
            if (obj is T) {
                return obj as T;
            }
            throw new Exception($"GetObjectFromPtr: Object is not {typeof(T).Name}.");
        }

        // === Functions =============================================================================================
        // ===========================================================================================================

        private void DefineNative(string name, int arity, GearsFunctionNativeDelegate onInvoke) {
            Globals.Set(name, GearsValue.CreateObjPtr(HeapAddObject(new GearsObjFunctionNative(name, arity, onInvoke))));
        }

        private void DefineMethod() {
            GearsValue methodPtr = Peek();
            GearsObjClosure method = GetObjectFromPtr<GearsObjClosure>(methodPtr);
            GearsObjClass objClass = GetObjectFromPtr<GearsObjClass>(Peek(1));
            objClass.Methods.Set(method.Function.Name, methodPtr);
            Pop();
        }

        private bool BindMethod(GearsObjClass classObj, string name) {
            if (!classObj.Methods.TryGet(name, out GearsValue method)) {
                return false;
            }
            int objPtr = HeapAddObject(new GearsObjBoundMethod(Peek(), HeapGetObject(method.AsObjPtr) as GearsObjClosure));
            Pop();
            Push(GearsValue.CreateObjPtr(objPtr));
            return true;
        }

        // --- Can probably merge a ton of code from the three call methods ---

        private void CallInvoke() {
            int argCount = ReadByte();
            string methodName = ReadConstantString();
            GearsValue receiverPtr = Peek(argCount);
            if (!(receiverPtr.IsObjPtr) || !(receiverPtr.AsObject(this) is GearsObjInstance instance)) {
                throw new GearsRuntimeException(0, "Attempted invoke to non-pointer or non-method.");
            }
            if (instance.Fields.TryGet(methodName, out GearsValue value)) {
                // check fields first 28.5.1:
                if ((!value.IsObjPtr) || !(HeapGetObject(value.AsObjPtr) is GearsObjClosure closure)) {
                    throw new GearsRuntimeException(0, $"Could not resolve method {methodName} in class {instance.Class}.");
                }
                if (closure.Function.Arity != argCount) {
                    throw new GearsRuntimeException(0, $"{closure} expects {closure.Function.Arity} arguments but was passed {argCount}.");
                }
                int bp = _SP - (closure.Function.Arity + 1);
                PushFrame(new GearsCallFrameClosure(closure, bp: bp));
            }
            else {
                InvokeFromClass(argCount, methodName, receiverPtr, instance.Class);
            }
        }

        private void InvokeFromClass(int argCount, string methodName, GearsValue receiverPtr, GearsObjClass objClass) {
            if (!objClass.Methods.TryGet(methodName, out GearsValue methodPtr)) {
                throw new GearsRuntimeException(0, $"{objClass} has no method with name {methodName}.");
            }
            if ((!methodPtr.IsObjPtr) || !(HeapGetObject(methodPtr.AsObjPtr) is GearsObjClosure method)) {
                throw new GearsRuntimeException(0, $"Could not resolve method {methodName} in class {objClass}.");
            }
            if (method.Function.Arity != argCount) {
                throw new GearsRuntimeException(0, $"{method} expects {method.Function.Arity} arguments but was passed {argCount}.");
            }
            int bp = _SP - (method.Function.Arity + 1);
            if (!receiverPtr.IsNil) {
                StackSet(bp, receiverPtr); // todo: this wipes out the method object. Is this bad?
            }
            PushFrame(new GearsCallFrameClosure(method, bp: bp));
        }

        private void CallInvokeSuper() {
            int argCount = ReadByte();
            // next instruction will always be OP_GET_UPVALUE (for the super class). we include it here:
            if (!(ReadByte() == (int)OP_GET_UPVALUE)) {
                throw new GearsRuntimeException(0, "OP_SUPER_INVOKE must be followed by OP_GET_UPVALUE.");
            }
            int slot = ReadShort();
            GearsObjUpvalue upvalue = (_OpenFrame as GearsCallFrameClosure).Closure.Upvalues[slot];
            GearsObjClass superclass = GetObjectFromPtr<GearsObjClass>(upvalue.IsClosed ? upvalue.Value : StackGet(upvalue.OriginalSP));
            string methodName = ReadConstantString();
            InvokeFromClass(argCount, methodName, GearsValue.NilValue, superclass);
        }

        private void Call() {
            int argCount = ReadByte();
            GearsValue ptr = Peek(argCount);
            if (!ptr.IsObjPtr) {
                throw new GearsRuntimeException(0, "Attempted call to non-pointer.");
            }
            GearsObj obj = HeapGetObject(ptr.AsObjPtr);
            if (obj is GearsObjFunction fn) { // this is not currently used - all fns currently wrapped in closures
                if (fn.Arity != argCount) {
                    throw new GearsRuntimeException(0, $"{fn} expects {fn.Arity} arguments but was passed {argCount}.");
                }
                int bp = _SP - (fn.Arity + 1);
                PushFrame(new GearsCallFrame(fn, bp: bp));
            }
            else if (obj is GearsObjClass classObj) {
                StackSet(_SP - argCount - 1, GearsValue.CreateObjPtr(HeapAddObject(new GearsObjInstance(classObj))));
                if (classObj.Methods.TryGet(InitString, out GearsValue initPtr)) {
                    if (!initPtr.IsObjPtr) {
                        throw new GearsRuntimeException(0, "Attempted call to non-pointer.");
                    }
                    GearsObjClosure initFn = HeapGetObject(initPtr.AsObjPtr) as GearsObjClosure;
                    PushFrame(new GearsCallFrame(initFn.Function, bp: _SP - argCount - 1));
                }
            }
            else if (obj is GearsObjClosure closure) {
                if (closure.Function.Arity != argCount) {
                    throw new GearsRuntimeException(0, $"{closure} expects {closure.Function.Arity} arguments but was passed {argCount}.");
                }
                int bp = _SP - (closure.Function.Arity + 1);
                PushFrame(new GearsCallFrameClosure(closure, bp: bp));
            }
            else if (obj is GearsObjFunctionNative native) {
                if (native.Arity != argCount) {
                    throw new GearsRuntimeException(0, $"{native} expects {native.Arity} arguments but was passed {argCount}.");
                }
                GearsValue[] args = new GearsValue[argCount];
                for (int i = argCount - 1; i >= 0; i++) {
                    args[i] = Pop();
                }
                Pop(); // pop the function signature
                Push(native.Invoke(args));
            }
            else if (obj is GearsObjBoundMethod method) {
                if (method.Method.Function.Arity != argCount) {
                    throw new GearsRuntimeException(0, $"{method} expects {method.Method.Function.Arity} arguments but was passed {argCount}.");
                }
                int bp = _SP - (method.Method.Function.Arity + 1);
                StackSet(bp, method.Receiver); // todo: this wipes out the method object. Is this bad?
                PushFrame(new GearsCallFrameClosure(method.Method, bp: bp));
            }
            else {
                throw new GearsRuntimeException(0, $"Unhandled call to object {obj}");
            }
        }
        // --- end merge candidates ---

        // === Closures ==============================================================================================
        // ===========================================================================================================

        private GearsObjUpvalue CaptureUpvalue(int sp) {
            GearsObjUpvalue previousUpvalue = null;
            GearsObjUpvalue currentUpValue = _OpenUpvalues;
            while (currentUpValue != null && currentUpValue.OriginalSP > sp) {
                previousUpvalue = currentUpValue;
                currentUpValue = currentUpValue.Next;
            }
            if (currentUpValue != null && currentUpValue.OriginalSP == sp) {
                return currentUpValue;
            }
            GearsObjUpvalue createdUpvalue = new GearsObjUpvalue(sp);
            createdUpvalue.Next = currentUpValue;
            if (previousUpvalue == null) {
                _OpenUpvalues = createdUpvalue;
            }
            else {
                previousUpvalue.Next = currentUpValue;
            }
            return createdUpvalue;
        }

        /// <summary>
        /// Starting with the passed stack slot, closes every open upvalue it can find that points to that slot or any
        /// slot above it on the stack. Close these upvalues, moving the values from the stack to the heap.
        /// </summary>
        private void CloseUpvalues(int sp) {
            // this function takes as a parameter an index of a stack slot. It closes every open upvalue it can find
            // pointing to that slot or any slot above it on the stack.
            // To do this, it walks the list of open upvalues, from top to bottom. If an upvalue's location points
            // into the range of slots we are closing, we close the upvalue. Otherwise, once we reach an upvalue
            // outside of that range, we know the rest will be too so we stop iterating.
            while (_OpenUpvalues != null && _OpenUpvalues.OriginalSP >= sp) {
                // To close an upvalue, we copy the variable' value into the closed field.
                GearsObjUpvalue upvalue = _OpenUpvalues;
                upvalue.Value = StackGet(upvalue.OriginalSP);
                upvalue.IsClosed = true;
                _OpenUpvalues = upvalue.Next;
            }
        }

        // === String speedup ========================================================================================
        // ===========================================================================================================

        public static ulong GetBitString(string value) {
            ulong bits = 0;
            int bitPosition = 0;
            for (int i = 0; i < value.Length; i++) {
                char ch = value[i];
                if (ch == '0') {
                    // encode as 000000
                }
                else if (ch >= '1' && ch <= '9') {
                    // encode as binary (000001 - 001001)
                    ulong bitValue = (ulong)(ch - '1');
                    bits |= (bitValue << bitPosition);
                }
                else if (ch >= 'A' && ch <= 'Z') {
                    // encode as 001010 - 100011
                    ulong bitValue = (ulong)(ch - 'A');
                    bits |= (bitValue << bitPosition);
                }
                else if (ch >= 'a' && ch <= 'z') {
                    // encode as 100100 - 111101
                    ulong bitValue = (ulong)(ch - 'a');
                    bits |= (bitValue << bitPosition);
                }
                else if (ch == '_') {
                    // encode as 111110
                    ulong bitValue = 62;
                    bits |= (bitValue << bitPosition);
                }
                else {
                    throw new Exception($"Cannot use character '{ch}' in string '{value}'.");
                }
                bitPosition += 6;
            }
            return bits;
        }
    }
}
