using Mono.Cecil;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;

namespace Swagger4WCF
{
    static public partial class YAML
    {
        public partial class Document
        {
            private partial class Content
            {
                static public Document Generate(TypeDefinition type, Documentation documentation)
                {
                    return new Document(type, new Content(type, documentation));
                }

                static public implicit operator string(Content compiler)
                {
                    return compiler == null ? null : compiler.ToString();
                }

                private StringBuilder m_Builder = new StringBuilder();
                private Tabulation m_Tabulation = new Tabulation("  ", 0);

                private Content(TypeDefinition type, Documentation documentation)
                {
                    this.Add("swagger: '2.0'");
                    this.Add("info:");
                    using (new Block(this))
                    {
                        this.Add("title: ", type.Name);
                        if (documentation[type] != null) { this.Add("description: ", documentation[type]); }
                        this.Add($"version: \"{ type.Module.Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Argument<string>(0) }\"");
                    }
                    this.Add("host: localhost");
                    this.Add("schemes:");
                    using (new Block(this))
                    {
                        this.Add("- http");
                        this.Add("- https");
                    }
                    this.Add("basePath: /", type.Name);
                    //this.Add("consumes:");
                    //using (new Block(this)) { this.Add("- application/json"); }
                    //this.Add("produces:");
                    //using (new Block(this)) { this.Add("- application/json"); }
                    this.Add("paths:");
                    var _methods = type.Methods
                        .Where(_Method => 
                                _Method.IsPublic 
                                && !_Method.IsStatic 
                                && _Method.GetCustomAttribute<OperationContractAttribute>() != null)
                        .OrderBy(_Method => _Method.MetadataToken.ToInt32())
                        .ToArray();

                    System.Collections.Generic.List<TypeDefinition> requestDefinitions = new System.Collections.Generic.List<TypeDefinition>();

                    using (new Block(this))
                    {   
                        foreach (var _method in _methods)
                        {
                                var parameters = _method.Parameters;
                                // var _type = parameter.ParameterType;
                                foreach (var parameter in parameters)
                                {
                                    var type1 = parameter.ParameterType;
                                    var resolver = type1.Resolve();
                                    if (!requestDefinitions.Contains(resolver))
                                        requestDefinitions.Add(resolver);
                                }

                            this.Add(_method, documentation);
                        }
                    }
                    this.Add("definitions:");
                    using (new Block(this))
                    {
                        var methods = _methods.Select(_Method => _Method.ReturnType)
                                           .Distinct()
                                           .OrderBy(_Type => _Type.Name)
                                           .Where(_Type =>
                                               !(_Type.Resolve() == _Type.Resolve().Module.Import(typeof(void)).Resolve())
                                               && !(_Type.Resolve() == _Type.Resolve().Module.Import(typeof(bool)).Resolve())
                                               && !(_Type.Resolve() == _Type.Resolve().Module.Import(typeof(string)).Resolve())
                                               && !(_Type.Resolve() == _Type.Resolve().Module.Import(typeof(int)).Resolve())
                                               && !(_Type.Resolve() == _Type.Resolve().Module.Import(typeof(long)).Resolve())
                                               && !(_Type.Resolve() == _Type.Resolve().Module.Import(typeof(DateTime)).Resolve()))
                                           .Select(_Type => _Type.IsArray ? _Type.GetElementType() : _Type).Distinct();
                        var methodLists = methods.ToList();
                        foreach (var _response in methods)
                        {
                            if (_response.Resolve() == _response.Module.Import(typeof(void)).Resolve()) { throw new NotSupportedException("Type 'System.Void' is not supported as return type."); }
                            if (_response.Resolve().GetCustomAttribute<DataContractAttribute>() == null) { throw new NotSupportedException(string.Format("Type '{0}' is not a data contract.", _response.FullName)); }

                            AddAllDefinitions(_response, documentation);                            
                        }

                        if (requestDefinitions.Count > 0)
                        {
                            requestDefinitions.ForEach(rd =>
                            {
                                AddAllDefinitions(rd, documentation);
                            });
                        }
                    }
                }

                private void AddAllDefinitions(TypeReference response, Documentation documentation)
                {
                    AddDefinitions(response, documentation);
                    BuildListProperties(response, documentation);                    
                }

                private System.Collections.Generic.List<string> _alreadyAddedDefs = new System.Collections.Generic.List<string>();

                private void BuildListProperties(TypeReference response, Documentation documentation)
                {
                    //old
                    //BuildGenericListProperties(response, documentation);
                    //BuildCommonProperties(response, documentation);


                    //BuildGenericListProperties
                    var genericList2 = response.GetGenericListProperties();
                    var genericList = response.GetCommonProperties();

                    var abc2 = genericList2.ToList();
                    var tt2 = abc2.Count;

                    var abc = genericList.ToList();
                    var tt = abc.Count;

                    //IOrderedEnumerable<PropertyDefinition> genericListAll
                    var genericListAll = new System.Collections.Generic.List<PropertyDefinition>();
                    genericListAll.AddRange(genericList2);
                    genericListAll.AddRange(genericList);
                    
                    foreach (var pp in genericListAll)
                    {
                        //check if not already added
                        if (!_alreadyAddedDefs.Contains(pp.PropertyType.FullName))
                        {
                            _alreadyAddedDefs.Add(pp.PropertyType.FullName);
                            AddDefinitions(pp.PropertyType, documentation);
                            BuildListProperties(pp.PropertyType, documentation);
                        }
                    }
                }

                private void Add(params string[] line)
                {
                    this.m_Builder.AppendLine(this.m_Tabulation.ToString() + string.Concat(line));
                }

                private void Add(MethodDefinition method, Documentation documentation)
                {
                    var _parameters = method.Parameters;
                    this.Add("/", method.Name, ":");
                    using (new Block(this))
                    {
                        var _attribute = method.GetCustomAttribute<WebInvokeAttribute>();
                        if (_attribute == null)
                        {
                            _attribute = method.GetCustomAttribute<WebGetAttribute>();
                            if (_attribute == null) { throw new NotSupportedException(); }
                            this.Add("get:");
                        }
                        else if (string.IsNullOrEmpty(_attribute.Value<string>("Method")))
                        {
                            throw new NotSupportedException();
                        }
                        else { this.Add(_attribute.Value<string>("Method").ToLower(), ":"); }
                        using (new Block(this))
                        {
                            this.Add("summary: ", method.Name);
                            if (documentation != null && documentation[method].Summary != null) { this.Add("description: ", documentation[method].Summary); }
                            this.Add("consumes:");
                            using (new Block(this))
                            {
                                if (_attribute.Value("RequestFormat") && _attribute.Value<WebMessageFormat>("RequestFormat") == WebMessageFormat.Json) { this.Add("- application/json"); }
                                else { this.Add("- application/xml"); }
                            }
                            this.Add("produces:");
                            using (new Block(this))
                            {
                                if (_attribute.Value("ResponseFormat") && _attribute.Value<WebMessageFormat>("ResponseFormat") == WebMessageFormat.Json) { this.Add("- application/json"); }
                                else { this.Add("- application/xml"); }
                            }
                            if (_parameters.Count > 0)
                            {
                                this.Add("parameters:");
                                using (new Block(this)) { foreach (var _parameter in _parameters) { this.Add(method, _parameter, documentation); } }
                                this.Add("tags:");
                                using (new Block(this)) { this.Add("- ", method.DeclaringType.Name); }
                            }
                            this.Add("responses:");
                            using (new Block(this))
                            {
                                this.Add("200:");
                                using (new Block(this))
                                {
                                    if (documentation != null && documentation[method].Response != null) { this.Add("description: ", documentation[method].Response); }
                                    else { this.Add("description: OK"); }
                                    if (method.ReturnType.Resolve() != method.Module.Import(typeof(void)).Resolve())
                                    {
                                        this.Add("schema:");
                                        using (new Block(this)) { this.Add(method.ReturnType); }
                                    }
                                }
                                //this.Add("400:");
                                //using (new Block(this))
                                //{
                                //    this.Add("description: Bad Request");
                                //    this.Add("schema:");
                                //    using (new Block(this)) { this.Add("type: \"string\""); }
                                //}
                                //this.Add("401:");
                                //using (new Block(this))
                                //{
                                //    this.Add("description: Unauthorized");
                                //    this.Add("schema:");
                                //    using (new Block(this)) { this.Add("type: \"string\""); }
                                //}
                                //this.Add("403:");
                                //using (new Block(this))
                                //{
                                //    this.Add("description: Forbidden");
                                //    this.Add("schema:");
                                //    using (new Block(this)) { this.Add("type: \"string\""); }
                                //}
                                //this.Add("409:");
                                //using (new Block(this))
                                //{
                                //    this.Add("description: Conflict");
                                //    this.Add("schema:");
                                //    using (new Block(this)) { this.Add("type: \"string\""); }
                                //}
                                //this.Add("500:");
                                //using (new Block(this))
                                //{
                                //    this.Add("description: Internal Server Error");
                                //    this.Add("schema:");
                                //    using (new Block(this)) { this.Add("type: \"string\""); }
                                //}
                                //this.Add("503:");
                                //using (new Block(this))
                                //{
                                //    this.Add("description: Service Unavailable");
                                //    this.Add("schema:");
                                //    using (new Block(this)) { this.Add("type: \"string\""); }
                                //}
                                this.Add("default:");
                                using (new Block(this))
                                {
                                    this.Add("description: failed");
                                    this.Add("schema:");
                                    using (new Block(this)) { this.Add("type: \"string\""); }
                                }
                            }
                        }
                    }
                }

                private void Add(MethodDefinition method, ParameterDefinition parameter, Documentation documentation)
                {
                    var _type = parameter.ParameterType;
                    this.Add("- name: ", parameter.Name);
                    using (new Block(this))
                    {
                        if (_type.Resolve() == _type.Module.Import(typeof(string)).Resolve() || _type.Resolve() == _type.Module.Import(typeof(int)).Resolve() || _type.Resolve() == _type.Module.Import(typeof(long)).Resolve() || _type.Resolve() == _type.Module.Import(typeof(DateTime)).Resolve() || _type.IsArray)
                        {
                            this.Add("in: query");
                            if (documentation != null && documentation[method, parameter] != null) { this.Add("description: ", documentation[method, parameter]); }
                            this.Add("required: ", parameter.ParameterType.IsValueType.ToString().ToLower());
                            this.Add(parameter.ParameterType);
                        }
                        else
                        {
                            this.Add("in: body");
                            if (documentation != null && documentation[method, parameter] != null) { this.Add("description: ", documentation[method, parameter]); }
                            this.Add("required: ", parameter.ParameterType.IsValueType.ToString().ToLower());
                            this.Add("schema:");
                            using (new Block(this)) { this.Add(parameter.ParameterType); }
                        }
                    }
                }

                private void Add(PropertyDefinition property, Documentation documentation)
                {
                    this.Add(property.Name, ":");
                    using (new Block(this))
                    {
                        this.Add(property.PropertyType);
                        if (documentation != null && documentation[property] != null) { this.Add("description: ", documentation[property]); }
                    }
                }

                private void Add(TypeReference type)
                {
                    if (type.Resolve() == type.Module.Import(typeof(string)).Resolve()) { this.Add("type: \"string\""); }
                    else if (type.Resolve() == type.Module.Import(typeof(bool)).Resolve()) { this.Add("type: \"boolean\""); }
                    else if (type.Resolve() == type.Module.Import(typeof(int)).Resolve())
                    {
                        this.Add("type: \"number\"");
                        this.Add("format: int32");
                    }
                    else if (type.Resolve() == type.Module.Import(typeof(long)).Resolve())
                    {
                        this.Add("type: \"number\"");
                        this.Add("format: int32");
                    }
                    else if (type.Resolve() == type.Module.Import(typeof(string)).Resolve())
                    {
                        //  allowedValues: array, boolean, integer, null, number, object, string
                        this.Add("type: \"string\"");
                        this.Add("format: date-time");
                    }
                    else if (type.IsArray)
                    {
                        this.Add("type: array");
                        this.Add("items:");
                        using (new Block(this)) { this.Add(type.GetElementType()); }
                    }
                    else if (type.Resolve() == type.Module.Import(typeof(string)).Resolve()) { }
                    else if (type.Resolve() == type.Module.Import(typeof(DateTime)).Resolve())
                    {
                        this.Add("type: \"string\"");
                        this.Add("format: date-time");
                    }
                    else if (type.Resolve() == type.Module.Import(typeof(Decimal)).Resolve())
                    {
                        this.Add("type: \"number\"");
                        this.Add("format: Decimal");
                    }
                    else if (type.Resolve() == type.Module.Import(typeof(System.Nullable)).Resolve())
                    {
                        this.Add("type: \"number\"");
                        this.Add("format: int32");
                    }
                    else if (type.Resolve() == type.Module.Import(typeof(System.Nullable<Int32>)).Resolve())
                    {
                        this.Add("type: \"number\"");
                        this.Add("format: int32");
                    }
                    else if (type.Resolve() == type.Module.Import(typeof(Char)).Resolve())
                    {
                        this.Add("type: \"string\"");
                    }
                    else
                    {
                        if (type.IsGenericInstance)
                        {
                            var abc4 = type.GetReflectionName();
                            this.Add("$ref: \"#/definitions/", abc4, "\"");
                        }
                        else
                        {
                            var abc = type.Resolve();
                            var abc2 = type.GetElementType();
                            var abc3 = type.Namespace;
                            //  abc = {WcfService1.CompositeType}
                            //  abc = {System.Collections.Generic.List`1}

                            var props = type.Resolve().Properties.ToList();
                            //.Where(_Property => _Property.GetCustomAttribute<DataMemberAttribute>() != null).ToList();
                            if (type.Resolve().GetCustomAttribute<DataContractAttribute>() == null)
                            {
                                throw new NotSupportedException(string.Format("Type '{0}' is not a data contract.", type.FullName));
                            }
                            this.Add("$ref: \"#/definitions/", type.Name, "\"");
                        }
                    }
                }

                override public string ToString()
                {
                    return this.m_Builder.ToString();
                }

                private void AddDefinitions(TypeReference _response, Documentation documentation)
                {
                    // https://stackoverflow.com/questions/16329710/maintaining-context-of-type-arguments-with-mono-cecil
                    //  https://stackoverflow.com/questions/16329710/maintaining-context-of-type-arguments-with-mono-cecil

                    var responseName = _response.GetReflectionName();
                    this.Add(responseName, ":");

                    using (new Block(this))
                    {
                        this.Add("type: \"object\"");
                        if (documentation != null)
                        {
                            var typeDefinition = _response.GetMethodDefinition();
                            //if (documentation[rrr2] != null)
                            this.Add(string.Concat("description: ", documentation[typeDefinition]));
                        }
                        this.Add("properties:");
                        using (new Block(this))
                        {
                            var pprs = _response.GetCustomAttributeProperties();
                            foreach (var _property in pprs)
                            {
                                this.Add(_property, documentation);
                            }
                        }
                    }
                }

                private void BuildGenericListProperties(TypeReference response, Documentation documentation)
                {
                    //new: add generic list
                    var genericList = response.GetGenericListProperties();
                    var abc2 = genericList.ToList();
                    var tt = abc2.Count;

                    foreach (var pp in genericList)
                    {
                        AddDefinitions(pp.PropertyType, documentation);

                        BuildGenericListProperties(pp.PropertyType, documentation);
                    }
                }
                
                private void BuildCommonProperties(TypeReference response, Documentation documentation)
                {
                    //new: add generic list
                    var genericList = response.GetCommonProperties();
                    var abc2 = genericList.ToList();
                    var tt = abc2.Count;

                    foreach (var pp in genericList)
                    {
                        //check if not already added
                        if (!_alreadyAddedDefs.Contains(pp.PropertyType.FullName))
                        {
                            _alreadyAddedDefs.Add(pp.PropertyType.FullName);
                            AddDefinitions(pp.PropertyType, documentation);
                            BuildCommonProperties(pp.PropertyType, documentation);
                        }
                        //AddDefinitions(pp.PropertyType, documentation);
                        //BuildCommonProperties(pp.PropertyType, documentation);
                    }
                }
            }
        }

        private static IOrderedEnumerable<PropertyDefinition> GetGenericListProperties(this TypeReference type)
        {
            Mono.Collections.Generic.Collection<PropertyDefinition> prps = null;
            if (type.IsGenericInstance)
            {
                var prps2 = (type as GenericInstanceType).GenericArguments.ToList();

                prps = (type as GenericInstanceType).GenericArguments.First()
                            .GetElementType().Resolve().Properties;

            }
            else
            {
                prps = type.Resolve().Properties;
            }


            var abc2 = prps.ToList();

            // get only generic properties
            var properties = prps
                                .Where(_Property => _Property.GetCustomAttribute<DataMemberAttribute>() != null
                                                    && _Property.PropertyType.IsGenericInstance
                                                    && !_Property.PropertyType.IsValueType
                                )
                                .OrderBy(_Property => _Property.MetadataToken.ToInt32());

            var abc = properties.ToList();

            return properties;
        }

        private static IOrderedEnumerable<PropertyDefinition> GetCommonProperties(this TypeReference type)
        {
            Mono.Collections.Generic.Collection<PropertyDefinition> prps = null;
            if (type.IsGenericInstance)
            {
                var prps2 = (type as GenericInstanceType).GenericArguments.ToList();

                prps = (type as GenericInstanceType).GenericArguments.First()
                            .GetElementType().Resolve().Properties;

            }
            else
            {
                prps = type.Resolve().Properties;
            }


            var abc2 = prps.ToList();

            // get only generic properties
            var properties = prps
                                .Where(_Property => _Property.GetCustomAttribute<DataMemberAttribute>() != null
                                                    && !_Property.PropertyType.IsGenericInstance
                                                    //&& !_Property.PropertyType.IsValueType
                                                    && _Property.PropertyType.MetadataType == MetadataType.Class
                                )
                                .OrderBy(_Property => _Property.MetadataToken.ToInt32());

            var abc = properties.ToList();

            return properties;
        }
        private static IOrderedEnumerable<PropertyDefinition> GetCustomAttributeProperties(this TypeReference type)
        {
            Mono.Collections.Generic.Collection<PropertyDefinition> prps = null;
            if (type.IsGenericInstance)
            {
                var prps2 = (type as GenericInstanceType).GenericArguments.ToList();

                prps = (type as GenericInstanceType).GenericArguments.First()
                            .GetElementType().Resolve().Properties;

            }
            else
            {
                prps = type.Resolve().Properties;
            }
            var abc2 = prps.ToList();

            var properties = prps
                                .Where(_Property => _Property.GetCustomAttribute<DataMemberAttribute>() != null)
                                .OrderBy(_Property => _Property.MetadataToken.ToInt32());

            var abc = properties.ToList();

            return properties;
        }

        private static TypeDefinition GetMethodDefinition(this TypeReference type)
        {
            TypeDefinition rrr = null;
            if (type.IsGenericInstance)
            {
                var gas2 = (type as GenericInstanceType).GenericArguments.ToList();

                var gas = (type as GenericInstanceType).GenericArguments
                    .First();
                //var yyy2 = gas.ToList();
                rrr = gas.Resolve();

                //rr.GenericParameters.Clear();
                //foreach (var p in gas)
                //{
                //    var yyy1 = p.GetElementType().Resolve();
                //    var rr2_3 = documentation[yyy1];

                //    var gp = new GenericParameter(p.FullName, yyy1);
                //    rr.GenericParameters.Add(gp);
                //}

                //var rr4 = rr.GenericParameters.ToList();
                //var xxx = rr4[0].Resolve();
                //var rr2_2 = documentation[rr4[0].Resolve()];
            }
            else
            {
                rrr = type.Resolve();
            }

            return rrr;
        }

        private static string GetReflectionName(this TypeReference type)
        {
            if (type.IsGenericInstance)
            {
                var genericInstance = (GenericInstanceType)type;
                var abc = genericInstance.GenericArguments.Select(p => p.GetReflectionName()).ToArray();

                var abc2 = string.Format("{0}.{1}[{2}]",
                    genericInstance.Namespace,
                    type.Name,
                    String.Join(",", abc));
                //var ttt = Type.GetType(abc2, true);


                return string.Format("{0}",
                        String.Join(",", abc));
            }
            return type.Name;
        }
    }
}
