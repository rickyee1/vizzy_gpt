using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModApi.Craft.Program;
using UnityEngine;

namespace VizzyGPT.Runtime.Adapters
{
    internal sealed class RuntimeContract
    {
        public RuntimeContract(
            UnityEngine.Object instance,
            MemberInfo flightProgramMember,
            object refreshTarget,
            MethodInfo refreshMethod)
        {
            Instance = instance;
            FlightProgramMember = flightProgramMember;
            RefreshTarget = refreshTarget;
            RefreshMethod = refreshMethod;
        }

        public UnityEngine.Object Instance { get; }

        public MemberInfo FlightProgramMember { get; }

        public object RefreshTarget { get; }

        public MethodInfo RefreshMethod { get; }
    }

    public sealed class RuntimeContractProbe
    {
        private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

        internal RuntimeContract ProbeEditor()
        {
            var candidates = new List<RuntimeContract>();

            foreach (var type in GetLoadedTypes())
            {
                if (type.IsAbstract || !typeof(UnityEngine.Object).IsAssignableFrom(type) ||
                    type.FullName == null ||
                    type.FullName.IndexOf("Vizzy", StringComparison.OrdinalIgnoreCase) < 0 ||
                    !TryFindFlightProgramMember(type, out var member))
                {
                    continue;
                }

                foreach (var instance in FindInstances(type))
                {
                    if (TryFindRefreshContract(type, instance, out var refreshTarget, out var refreshMethod))
                    {
                        candidates.Add(new RuntimeContract(instance, member, refreshTarget, refreshMethod));
                    }
                }
            }

            if (candidates.Count != 1)
            {
                return null;
            }

            var contract = candidates[0];
            Debug.LogFormat(
                "VizzyGPT resolved Vizzy runtime contract: {0}.{1}",
                contract.Instance.GetType().FullName,
                contract.FlightProgramMember.Name);
            return contract;
        }

        internal bool TryFindFlightProgramFallback(out UnityEngine.Object instance, out MemberInfo member)
        {
            var candidates = new List<(UnityEngine.Object Instance, MemberInfo Member)>();

            foreach (var type in GetLoadedTypes())
            {
                if (type.IsAbstract || !typeof(UnityEngine.Object).IsAssignableFrom(type) ||
                    type.FullName == null ||
                    type.FullName.IndexOf("FlightProgram", StringComparison.OrdinalIgnoreCase) < 0 ||
                    !TryFindFlightProgramMember(type, out var candidateMember))
                {
                    continue;
                }

                foreach (var candidate in FindInstances(type))
                {
                    candidates.Add((candidate, candidateMember));
                }
            }

            if (candidates.Count == 1)
            {
                instance = candidates[0].Instance;
                member = candidates[0].Member;
                Debug.LogFormat(
                    "VizzyGPT resolved flight program contract: {0}.{1}",
                    instance.GetType().FullName,
                    member.Name);
                return true;
            }

            instance = null;
            member = null;
            return false;
        }

        internal static bool TryFindFlightProgramMember(Type type, out MemberInfo member)
        {
            var property = type.GetProperty("FlightProgram", PublicInstance);
            if (property != null && property.GetGetMethod() != null &&
                typeof(FlightProgram).IsAssignableFrom(property.PropertyType))
            {
                member = property;
                return true;
            }

            var field = type.GetField("FlightProgram", PublicInstance);
            if (field != null && typeof(FlightProgram).IsAssignableFrom(field.FieldType))
            {
                member = field;
                return true;
            }

            member = null;
            return false;
        }

        private static IEnumerable<Type> GetLoadedTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types.Where(type => type != null).ToArray();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type != null)
                    {
                        yield return type;
                    }
                }
            }
        }

        private static IEnumerable<UnityEngine.Object> FindInstances(Type type)
        {
            UnityEngine.Object[] instances;
            try
            {
                instances = Resources.FindObjectsOfTypeAll(type);
            }
            catch (Exception)
            {
                yield break;
            }

            foreach (var instance in instances)
            {
                if (instance != null)
                {
                    yield return instance;
                }
            }
        }

        private static bool TryFindRefreshContract(
            Type editorType,
            UnityEngine.Object editor,
            out object refreshTarget,
            out MethodInfo refreshMethod)
        {
            refreshMethod = FindPublicRefreshMethod(editorType);
            if (refreshMethod != null)
            {
                refreshTarget = editor;
                return true;
            }

            var controller = editorType
                .GetField("_controller", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(editor);
            if (controller != null)
            {
                refreshMethod = FindPublicRefreshMethod(controller.GetType());
                if (refreshMethod != null)
                {
                    refreshTarget = controller;
                    return true;
                }
            }

            refreshTarget = null;
            return false;
        }

        private static MethodInfo FindPublicRefreshMethod(Type type)
        {
            return new[] { "RefreshUI", "RefreshCategory", "Refresh", "Rebuild" }
                .Select(name => type.GetMethod(name, PublicInstance, null, Type.EmptyTypes, null))
                .FirstOrDefault(method => method != null);
        }
    }
}
