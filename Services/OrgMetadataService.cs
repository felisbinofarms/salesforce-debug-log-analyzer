using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Services
{
    /// <summary>
    /// Fetches and caches Salesforce org metadata to enrich log analysis with human-readable context.
    /// Replaces cryptic IDs and API names with actual labels, record names, and locations.
    /// </summary>
    public class OrgMetadataService
    {
        private readonly SalesforceApiService _apiService;
        private readonly string _cacheDirectory;
        private OrgMetadata? _cachedMetadata;
        private const int CacheExpirationDays = 7;

        public OrgMetadataService(SalesforceApiService apiService)
        {
            _apiService = apiService;
            
            // Store metadata in %APPDATA%/BlackWidow/orgs/{orgId}/
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _cacheDirectory = Path.Combine(appData, "BlackWidow", "orgs");
            Directory.CreateDirectory(_cacheDirectory);
        }

        /// <summary>
        /// Gets org metadata from cache or fetches fresh if expired/missing
        /// </summary>
        public async Task<OrgMetadata> GetOrgMetadataAsync(bool forceRefresh = false)
        {
            // Return cached if valid
            if (!forceRefresh && _cachedMetadata != null)
            {
                return _cachedMetadata;
            }

            // Get org ID from current connection
            var orgId = await GetOrgIdAsync();
            var cacheFile = Path.Combine(_cacheDirectory, orgId, "metadata.json");

            // Load from disk cache if exists and not expired
            if (!forceRefresh && File.Exists(cacheFile))
            {
                var fileInfo = new FileInfo(cacheFile);
                if (DateTime.Now - fileInfo.LastWriteTime < TimeSpan.FromDays(CacheExpirationDays))
                {
                    var json = await File.ReadAllTextAsync(cacheFile);
                    _cachedMetadata = JsonSerializer.Deserialize<OrgMetadata>(json);
                    if (_cachedMetadata != null)
                    {
                        return _cachedMetadata;
                    }
                }
            }

            // Fetch fresh metadata from Salesforce
            _cachedMetadata = await FetchOrgMetadataAsync(orgId);

            // Cache to disk
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            var jsonToSave = JsonSerializer.Serialize(_cachedMetadata, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cacheFile, jsonToSave);

            return _cachedMetadata;
        }

        /// <summary>
        /// Fetches comprehensive org metadata from Salesforce APIs
        /// </summary>
        private async Task<OrgMetadata> FetchOrgMetadataAsync(string orgId)
        {
            var metadata = new OrgMetadata
            {
                OrgId = orgId,
                FetchedAt = DateTime.Now
            };

            try
            {
                // Fetch in parallel for speed
                var tasks = new List<Task>
                {
                    FetchObjectsAsync(metadata),
                    FetchUsersAsync(metadata),
                    FetchApexClassesAsync(metadata),
                    FetchApexTriggersAsync(metadata),
                    FetchFlowsAsync(metadata),
                    FetchRecordTypesAsync(metadata)
                };

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                // Log error but return partial metadata
                Console.WriteLine($"Error fetching metadata: {ex.Message}");
            }

            return metadata;
        }

        /// <summary>
        /// Fetches all standard and custom objects with their labels
        /// </summary>
        private async Task FetchObjectsAsync(OrgMetadata metadata)
        {
            try
            {
                // Use Salesforce Describe API to get all objects
                var response = await _apiService.ExecuteRequestAsync<DescribeGlobalResponse>(
                    "GET", 
                    "/services/data/v59.0/sobjects"
                );

                foreach (var sobject in response.SObjects)
                {
                    metadata.Objects[sobject.Name] = new SObjectMetadata
                    {
                        ApiName = sobject.Name,
                        Label = sobject.Label,
                        LabelPlural = sobject.LabelPlural,
                        IsCustom = sobject.Custom,
                        KeyPrefix = sobject.KeyPrefix
                    };

                    // Fetch fields for important objects (Case, Account, Opportunity, etc.)
                    if (IsImportantObject(sobject.Name))
                    {
                        await FetchObjectFieldsAsync(metadata, sobject.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching objects: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetches fields for a specific object
        /// </summary>
        private async Task FetchObjectFieldsAsync(OrgMetadata metadata, string objectName)
        {
            try
            {
                var response = await _apiService.ExecuteRequestAsync<DescribeSObjectResponse>(
                    "GET",
                    $"/services/data/v59.0/sobjects/{objectName}/describe"
                );

                if (!metadata.Objects.ContainsKey(objectName))
                    return;

                var objectMeta = metadata.Objects[objectName];
                objectMeta.Fields = new Dictionary<string, FieldMetadata>();

                foreach (var field in response.Fields)
                {
                    objectMeta.Fields[field.Name] = new FieldMetadata
                    {
                        ApiName = field.Name,
                        Label = field.Label,
                        Type = field.Type,
                        IsCustom = field.Custom
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching fields for {objectName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetches all active users
        /// </summary>
        private async Task FetchUsersAsync(OrgMetadata metadata)
        {
            try
            {
                var query = "SELECT Id, Name, Username, Email FROM User WHERE IsActive = true";
                var response = await _apiService.QueryAsync<UserRecord>(query);

                foreach (var user in response.Records)
                {
                    metadata.Users[user.Id] = new UserMetadata
                    {
                        Id = user.Id,
                        Name = user.Name,
                        Username = user.Username,
                        Email = user.Email
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching users: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetches all Apex classes with their methods
        /// </summary>
        private async Task FetchApexClassesAsync(OrgMetadata metadata)
        {
            try
            {
                var query = "SELECT Id, Name, NamespacePrefix, ApiVersion FROM ApexClass";
                var response = await _apiService.QueryAsync<ApexClassRecord>(query);

                foreach (var cls in response.Records)
                {
                    var fullName = string.IsNullOrEmpty(cls.NamespacePrefix) 
                        ? cls.Name 
                        : $"{cls.NamespacePrefix}.{cls.Name}";

                    metadata.ApexClasses[fullName] = new ApexClassMetadata
                    {
                        Id = cls.Id,
                        Name = cls.Name,
                        FullName = fullName,
                        Namespace = cls.NamespacePrefix
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching Apex classes: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetches all Apex triggers
        /// </summary>
        private async Task FetchApexTriggersAsync(OrgMetadata metadata)
        {
            try
            {
                var query = "SELECT Id, Name, TableEnumOrId, Status FROM ApexTrigger";
                var response = await _apiService.QueryAsync<ApexTriggerRecord>(query);

                foreach (var trigger in response.Records)
                {
                    metadata.ApexTriggers[trigger.Name] = new ApexTriggerMetadata
                    {
                        Id = trigger.Id,
                        Name = trigger.Name,
                        ObjectName = trigger.TableEnumOrId,
                        IsActive = trigger.Status == "Active"
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching triggers: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetches all flows
        /// </summary>
        private async Task FetchFlowsAsync(OrgMetadata metadata)
        {
            try
            {
                var query = "SELECT Id, ApiName, Label, ProcessType, Status FROM FlowDefinitionView WHERE IsActive = true";
                var response = await _apiService.QueryAsync<FlowRecord>(query);

                foreach (var flow in response.Records)
                {
                    metadata.Flows[flow.ApiName] = new FlowMetadata
                    {
                        Id = flow.Id,
                        ApiName = flow.ApiName,
                        Label = flow.Label,
                        Type = flow.ProcessType
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching flows: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetches record types for all objects
        /// </summary>
        private async Task FetchRecordTypesAsync(OrgMetadata metadata)
        {
            try
            {
                var query = "SELECT Id, Name, DeveloperName, SobjectType FROM RecordType WHERE IsActive = true";
                var response = await _apiService.QueryAsync<RecordTypeRecord>(query);

                foreach (var rt in response.Records)
                {
                    var key = $"{rt.SobjectType}.{rt.DeveloperName}";
                    metadata.RecordTypes[key] = new RecordTypeMetadata
                    {
                        Id = rt.Id,
                        Name = rt.Name,
                        DeveloperName = rt.DeveloperName,
                        ObjectName = rt.SobjectType
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching record types: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current org ID
        /// </summary>
        private async Task<string> GetOrgIdAsync()
        {
            try
            {
                // Query Organization to get OrgId
                var query = "SELECT Id, Name FROM Organization";
                var response = await _apiService.QueryAsync<OrganizationRecord>(query);
                return response.Records.FirstOrDefault()?.Id ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        /// <summary>
        /// Determines if an object is important enough to fetch field metadata
        /// </summary>
        private bool IsImportantObject(string objectName)
        {
            var importantObjects = new[]
            {
                "Case", "Account", "Contact", "Lead", "Opportunity",
                "Task", "Event", "User", "Order", "Product2",
                "Quote", "Contract", "Asset"
            };

            return importantObjects.Contains(objectName) || objectName.EndsWith("__c");
        }

        // ========== ENRICHMENT METHODS ==========

        /// <summary>
        /// Enriches a log entry point with metadata (e.g., "CaseTrigger" → "CaseTrigger.apxt on Case")
        /// </summary>
        public string EnrichEntryPoint(string entryPoint, OrgMetadata metadata)
        {
            if (string.IsNullOrEmpty(entryPoint))
                return entryPoint;

            // Extract trigger name (e.g., "CaseTrigger on Case" → "CaseTrigger")
            var triggerName = entryPoint.Split(' ').FirstOrDefault();
            if (triggerName != null && metadata.ApexTriggers.TryGetValue(triggerName, out var trigger))
            {
                return $"{triggerName}.apxt on {trigger.ObjectName}";
            }

            // Extract class name (e.g., "CaseController.getCases" → "CaseController")
            var className = entryPoint.Split('.').FirstOrDefault();
            if (className != null && metadata.ApexClasses.TryGetValue(className, out var cls))
            {
                return $"{cls.FullName}.{entryPoint.Split('.').LastOrDefault()}";
            }

            return entryPoint;
        }

        /// <summary>
        /// Enriches a user ID with actual name (e.g., "005xx" → "John Smith (john@example.com)")
        /// </summary>
        public string EnrichUserId(string userId, OrgMetadata metadata)
        {
            if (string.IsNullOrEmpty(userId) || !userId.StartsWith("005"))
                return userId;

            if (metadata.Users.TryGetValue(userId, out var user))
            {
                return $"{user.Name} ({user.Email})";
            }

            return userId;
        }

        /// <summary>
        /// Enriches a record ID with record name/title (e.g., "500xx" → "Case-00001234: Need help")
        /// </summary>
        public async Task<string> EnrichRecordIdAsync(string recordId, OrgMetadata metadata)
        {
            if (string.IsNullOrEmpty(recordId) || recordId.Length < 15)
                return recordId;

            // Determine object type from key prefix
            var keyPrefix = recordId.Substring(0, 3);
            var objectMeta = metadata.Objects.Values.FirstOrDefault(o => o.KeyPrefix == keyPrefix);
            
            if (objectMeta == null)
                return recordId;

            try
            {
                // Query for record name/title
                var nameField = objectMeta.ApiName == "Case" ? "CaseNumber" : "Name";
                var query = $"SELECT {nameField} FROM {objectMeta.ApiName} WHERE Id = '{recordId}' LIMIT 1";
                var response = await _apiService.QueryAsync<Dictionary<string, object>>(query);
                
                if (response.Records.Count > 0 && response.Records[0].TryGetValue(nameField, out var name))
                {
                    return $"{name} ({objectMeta.Label})";
                }
            }
            catch
            {
                // Fallback to just showing object type
            }

            return $"{recordId} ({objectMeta.Label})";
        }
    }

    // ========== METADATA MODELS ==========

    public class OrgMetadata
    {
        public string OrgId { get; set; } = string.Empty;
        public DateTime FetchedAt { get; set; }
        public Dictionary<string, SObjectMetadata> Objects { get; set; } = new();
        public Dictionary<string, UserMetadata> Users { get; set; } = new();
        public Dictionary<string, ApexClassMetadata> ApexClasses { get; set; } = new();
        public Dictionary<string, ApexTriggerMetadata> ApexTriggers { get; set; } = new();
        public Dictionary<string, FlowMetadata> Flows { get; set; } = new();
        public Dictionary<string, RecordTypeMetadata> RecordTypes { get; set; } = new();
    }

    public class SObjectMetadata
    {
        public string ApiName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string LabelPlural { get; set; } = string.Empty;
        public bool IsCustom { get; set; }
        public string KeyPrefix { get; set; } = string.Empty;
        public Dictionary<string, FieldMetadata>? Fields { get; set; }
    }

    public class FieldMetadata
    {
        public string ApiName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsCustom { get; set; }
    }

    public class UserMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class ApexClassMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Namespace { get; set; }
    }

    public class ApexTriggerMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public class FlowMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string ApiName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    public class RecordTypeMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DeveloperName { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
    }

    // ========== API RESPONSE MODELS ==========

    public class DescribeGlobalResponse
    {
        public List<SObjectDescribe> SObjects { get; set; } = new();
    }

    public class SObjectDescribe
    {
        public string Name { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string LabelPlural { get; set; } = string.Empty;
        public bool Custom { get; set; }
        public string KeyPrefix { get; set; } = string.Empty;
    }

    public class DescribeSObjectResponse
    {
        public List<FieldDescribe> Fields { get; set; } = new();
    }

    public class FieldDescribe
    {
        public string Name { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool Custom { get; set; }
    }

    public class UserRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class ApexClassRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? NamespacePrefix { get; set; }
        public double ApiVersion { get; set; }
    }

    public class ApexTriggerRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TableEnumOrId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class FlowRecord
    {
        public string Id { get; set; } = string.Empty;
        public string ApiName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string ProcessType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class RecordTypeRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DeveloperName { get; set; } = string.Empty;
        public string SobjectType { get; set; } = string.Empty;
    }

    public class OrganizationRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
