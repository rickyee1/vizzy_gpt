using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using ModApi.Craft.Program;
using UnityEngine;

namespace VizzyGPT.Runtime.Adapters
{
    internal sealed class RefreshContract
    {
        public RefreshContract(object target, MethodInfo method)
        {
            Target = target;
            Method = method;
        }

        public object Target { get; }

        public MethodInfo Method { get; }
    }

    internal sealed class RuntimeContract
    {
        public RuntimeContract(
            UnityEngine.Object instance,
            MemberInfo flightProgramMember,
            object refreshTarget,
            MethodInfo refreshMethod)
            : this(instance, flightProgramMember, refreshTarget, refreshMethod, null)
        {
        }

        public RuntimeContract(
            UnityEngine.Object instance,
            MemberInfo flightProgramMember,
            object refreshTarget,
            MethodInfo refreshMethod,
            MethodInfo programLoadMethod)
        {
            Instance = instance;
            FlightProgramMember = flightProgramMember;
            RefreshTarget = refreshTarget;
            RefreshMethod = refreshMethod;
            ProgramLoadMethod = programLoadMethod;
        }

        public UnityEngine.Object Instance { get; }

        public MemberInfo FlightProgramMember { get; }

        public object RefreshTarget { get; }

        public MethodInfo RefreshMethod { get; }

        public MethodInfo ProgramLoadMethod { get; }
    }

    public sealed class RuntimeContractProbe
    {
        private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
        private readonly Func<UnityEngine.Object, RefreshContract> privateRefreshContractResolver;

        internal RuntimeContractProbe(Func<UnityEngine.Object, RefreshContract> privateRefreshContractResolver)
        {
            this.privateRefreshContractResolver = privateRefreshContractResolver ??
                throw new ArgumentNullException(nameof(privateRefreshContractResolver));
        }

        internal RuntimeContract ProbeEditor(out RuntimeCompatibilityResult compatibility)
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

                var programLoadMethod = FindPublicProgramLoadMethod(type);
                foreach (var instance in FindInstances(type))
                {
                    if (!IsActiveSceneComponent(instance))
                    {
                        continue;
                    }

                    if (programLoadMethod != null)
                    {
                        candidates.Add(new RuntimeContract(
                            instance,
                            member,
                            null,
                            null,
                            programLoadMethod));
                    }
                    else if (TryFindRefreshContract(type, instance, out var refreshTarget, out var refreshMethod))
                    {
                        candidates.Add(new RuntimeContract(
                            instance,
                            member,
                            refreshTarget,
                            refreshMethod));
                    }
                }
            }

            if (candidates.Count == 0)
            {
                compatibility = RuntimeCompatibilityResult.EditorUnavailable();
                return null;
            }

            if (candidates.Count > 1)
            {
                compatibility = RuntimeCompatibilityResult.AmbiguousContract(
                    candidates.Select(SummarizeInstance).ToArray());
                return null;
            }

            var contract = candidates[0];
            compatibility = RuntimeCompatibilityResult.Compatible(Summarize(contract));
            Debug.LogFormat(
                "VizzyGPT resolved Vizzy runtime contract: {0}.{1}",
                contract.Instance.GetType().FullName,
                contract.FlightProgramMember.Name);
            return contract;
        }

        private static string Summarize(RuntimeContract contract)
        {
            return contract.Instance.GetType().FullName + "." + contract.FlightProgramMember.Name;
        }

        private static string SummarizeInstance(RuntimeContract contract)
        {
            return Summarize(contract) + "@" + contract.Instance.GetInstanceID();
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

        public static bool TryFindFlightProgramOnCraft(object craftScript, out FlightProgram program)
        {
            program = null;
            if (craftScript == null)
            {
                Debug.LogWarning("VizzyGPT flight program probe: active craft script is null.");
                return false;
            }

            var scriptsProperty = craftScript.GetType().GetProperty("FlightProgramScripts", PublicInstance);
            if (!(scriptsProperty?.GetValue(craftScript, null) is IEnumerable scripts))
            {
                Debug.LogWarningFormat(
                    "VizzyGPT flight program probe: {0} has no enumerable FlightProgramScripts property.",
                    craftScript.GetType().FullName);
                return false;
            }

            var activeCommandPod = craftScript.GetType()
                .GetProperty("ActiveCommandPod", PublicInstance)
                ?.GetValue(craftScript, null);
            var activePart = activeCommandPod?.GetType()
                .GetProperty("Part", PublicInstance)
                ?.GetValue(activeCommandPod, null);
            var candidates = new List<(FlightProgram Program, object Part)>();
            var scriptCount = 0;
            foreach (var script in scripts)
            {
                scriptCount++;
                if (script == null || !TryFindFlightProgramMember(script.GetType(), out var member))
                {
                    Debug.LogWarningFormat(
                        "VizzyGPT flight program probe: entry {0} has no public FlightProgram member.",
                        script?.GetType().FullName ?? "<null>");
                    continue;
                }

                var value = member is PropertyInfo property
                    ? property.GetValue(script, null)
                    : ((FieldInfo)member).GetValue(script);
                if (value is FlightProgram candidate)
                {
                    var partScript = script.GetType()
                        .GetProperty("PartScript", PublicInstance)
                        ?.GetValue(script, null);
                    var part = partScript?.GetType()
                        .GetProperty("Data", PublicInstance)
                        ?.GetValue(partScript, null);
                    candidates.Add((candidate, part));
                }
                else
                {
                    Debug.LogWarningFormat(
                        "VizzyGPT flight program probe: {0}.{1} returned {2}.",
                        script.GetType().FullName,
                        member.Name,
                        value?.GetType().FullName ?? "null");
                }
            }

            var activeCandidates = candidates
                .Where(candidate => activePart != null && ReferenceEquals(candidate.Part, activePart))
                .ToArray();
            if (activeCandidates.Length == 1)
            {
                program = activeCandidates[0].Program;
                return true;
            }

            if (candidates.Count != 1)
            {
                Debug.LogWarningFormat(
                    "VizzyGPT flight program probe: craft type {0}, script entries {1}, valid programs {2}.",
                    craftScript.GetType().FullName,
                    scriptCount,
                    candidates.Count);
                return false;
            }

            program = candidates[0].Program;
            return true;
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

        private static bool IsActiveSceneComponent(UnityEngine.Object instance)
        {
            return instance is Component component &&
                component.gameObject.scene.IsValid() &&
                component.gameObject.activeInHierarchy;
        }

        private bool TryFindRefreshContract(
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

            var privateRefreshContract = privateRefreshContractResolver(editor);
            if (privateRefreshContract != null)
            {
                refreshTarget = privateRefreshContract.Target;
                refreshMethod = privateRefreshContract.Method;
                return true;
            }

            refreshTarget = null;
            return false;
        }

        internal static MethodInfo FindPublicRefreshMethod(Type type)
        {
            return new[] { "RefreshUI", "RefreshCategory", "Refresh", "Rebuild" }
                .Select(name => type.GetMethod(name, PublicInstance, null, Type.EmptyTypes, null))
                .FirstOrDefault(method => method != null);
        }

        private static MethodInfo FindPublicProgramLoadMethod(Type type)
        {
            return type.GetMethod(
                "LoadFlightProgram",
                PublicInstance,
                null,
                new[] { typeof(XElement) },
                null);
        }
    }
}
