using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace VizzyGPT.Core.Patching
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PatchOperationType
    {
        [EnumMember(Value = "addVariable")]
        AddVariable,
        [EnumMember(Value = "renameVariable")]
        RenameVariable,
        [EnumMember(Value = "removeVariable")]
        RemoveVariable,
        [EnumMember(Value = "insertBefore")]
        InsertBefore,
        [EnumMember(Value = "insertAfter")]
        InsertAfter,
        [EnumMember(Value = "insertChild")]
        InsertChild,
        [EnumMember(Value = "replaceNode")]
        ReplaceNode,
        [EnumMember(Value = "removeNode")]
        RemoveNode,
        [EnumMember(Value = "moveNode")]
        MoveNode,
        [EnumMember(Value = "updateAttribute")]
        UpdateAttribute
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PatchOperation
    {
        [JsonConstructor]
        public PatchOperation(
            PatchOperationType type,
            NodeSelector? target = null,
            NodeSelector? destination = null,
            NodeSpec? node = null,
            string? name = null,
            string? newName = null,
            string? attribute = null,
            string? value = null)
        {
            if (!Enum.IsDefined(typeof(PatchOperationType), type))
            {
                throw new PatchApplyException("Unknown patch operation type.");
            }

            Type = type;
            Target = target;
            Destination = destination;
            Node = node;
            Name = name;
            NewName = newName;
            Attribute = attribute;
            Value = value;
            ValidateFields();
        }

        [JsonProperty("type", Required = Required.Always)]
        public PatchOperationType Type { get; }

        [JsonProperty("target", NullValueHandling = NullValueHandling.Ignore)]
        public NodeSelector? Target { get; }

        [JsonProperty("destination", NullValueHandling = NullValueHandling.Ignore)]
        public NodeSelector? Destination { get; }

        [JsonProperty("node", NullValueHandling = NullValueHandling.Ignore)]
        public NodeSpec? Node { get; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string? Name { get; }

        [JsonProperty("newName", NullValueHandling = NullValueHandling.Ignore)]
        public string? NewName { get; }

        [JsonProperty("attribute", NullValueHandling = NullValueHandling.Ignore)]
        public string? Attribute { get; }

        [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)]
        public string? Value { get; }

        private void ValidateFields()
        {
            switch (Type)
            {
                case PatchOperationType.AddVariable:
                    Require(Name, "name");
                    Reject(Target, "target");
                    Reject(Destination, "destination");
                    Reject(Node, "node");
                    Reject(NewName, "newName");
                    Reject(Attribute, "attribute");
                    break;
                case PatchOperationType.RenameVariable:
                    Require(Name, "name");
                    Require(NewName, "newName");
                    Reject(Target, "target");
                    Reject(Destination, "destination");
                    Reject(Node, "node");
                    Reject(Attribute, "attribute");
                    Reject(Value, "value");
                    break;
                case PatchOperationType.RemoveVariable:
                    Require(Name, "name");
                    Reject(Target, "target");
                    Reject(Destination, "destination");
                    Reject(Node, "node");
                    Reject(NewName, "newName");
                    Reject(Attribute, "attribute");
                    Reject(Value, "value");
                    break;
                case PatchOperationType.InsertBefore:
                case PatchOperationType.InsertAfter:
                case PatchOperationType.InsertChild:
                case PatchOperationType.ReplaceNode:
                    Require(Target, "target");
                    Require(Node, "node");
                    Reject(Destination, "destination");
                    Reject(Name, "name");
                    Reject(NewName, "newName");
                    Reject(Attribute, "attribute");
                    Reject(Value, "value");
                    break;
                case PatchOperationType.RemoveNode:
                    Require(Target, "target");
                    Reject(Destination, "destination");
                    Reject(Node, "node");
                    Reject(Name, "name");
                    Reject(NewName, "newName");
                    Reject(Attribute, "attribute");
                    Reject(Value, "value");
                    break;
                case PatchOperationType.MoveNode:
                    Require(Target, "target");
                    Require(Destination, "destination");
                    Reject(Node, "node");
                    Reject(Name, "name");
                    Reject(NewName, "newName");
                    Reject(Attribute, "attribute");
                    Reject(Value, "value");
                    break;
                case PatchOperationType.UpdateAttribute:
                    Require(Target, "target");
                    Require(Attribute, "attribute");
                    Require(Value, "value");
                    Reject(Destination, "destination");
                    Reject(Node, "node");
                    Reject(Name, "name");
                    Reject(NewName, "newName");
                    break;
                default:
                    throw new PatchApplyException("Unknown patch operation type.");
            }
        }

        private static void Require(object? value, string field)
        {
            if (value == null)
            {
                throw new PatchApplyException("Patch operation requires field '" + field + "'.");
            }
        }

        private static void Reject(object? value, string field)
        {
            if (value != null)
            {
                throw new PatchApplyException("Patch operation does not permit field '" + field + "'.");
            }
        }
    }
}
