# Black Widow Testing Guide

## Overview
This document outlines the testing scenarios for the Salesforce Debug Log Analyzer (Black Widow), focusing on its unique transaction grouping and phase detection capabilities.

---

## Test Scenarios

### 1. Single Log Analysis (Baseline)

**Objective:** Verify individual log parsing works correctly

**Setup:**
- Load a single debug log file (e.g., `apex-07LbV000006nBzmUAE.log`)

**Expected Behavior:**
- ✅ Log parses successfully
- ✅ Execution tree is built with proper hierarchy
- ✅ SOQL and DML operations extracted
- ✅ Governor limits parsed correctly
- ✅ Method statistics calculated
- ✅ Plain English summary generated
- ✅ Recommendations provided based on patterns

**Pass Criteria:**
- No parsing errors
- Summary accurately describes execution
- All metrics match log content

---

### 2. Transaction Grouping - Happy Path

**Objective:** Verify logs from same user action are grouped correctly

**Setup:**
- Create folder with 3-5 logs from same user within 10 seconds
- All logs should have same UserId
- Logs should have sequential timestamps

**Test Data Structure:**
```
TestFolder/
├── apex-07L001.log (21:57:43.001 - CaseTrigger.beforeUpdate)
├── apex-07L002.log (21:57:43.252 - Flow: Case_Validation)
├── apex-07L003.log (21:57:44.452 - CaseTrigger.afterUpdate)
└── apex-07L004.log (21:57:46.132 - AccountTrigger.afterUpdate)
```

**Expected Behavior:**
- ✅ All 4 logs grouped into single `LogGroup`
- ✅ `IsSingleLog = false`
- ✅ Total duration calculated across all logs
- ✅ Aggregate SOQL/DML metrics summed correctly
- ✅ Display shows "Transaction Group - 4 logs"

**Pass Criteria:**
- `LogGroups.Count == 1`
- `LogGroups[0].Logs.Count == 4`
- Total duration = (last log end time - first log start time)

---

### 3. Phase Detection - Backend vs Frontend

**Objective:** Verify backend and frontend logs are separated into phases

**Setup:**
- Folder with mixed log types:
  - 3 backend logs (triggers/flows)
  - 3 frontend logs (Lightning controllers)

**Test Data:**
```
Backend Logs:
- CaseTrigger.beforeUpdate (21:57:43.001)
- Flow: Case_Validation (21:57:43.252)
- CaseTrigger.afterUpdate (21:57:44.452)

Frontend Logs:
- RelatedCasesController.getCases() (21:57:51.235)
- ActivityTimelineController.getActivities() (21:57:51.240)
- KnowledgeController.getSuggestions() (21:57:51.245)
```

**Expected Behavior:**
- ✅ 2 phases detected: Backend and Frontend
- ✅ Backend phase contains 3 logs
- ✅ Frontend phase contains 3 logs
- ✅ Gap between phases calculated (if > 100ms)
- ✅ Phase durations summed correctly

**Pass Criteria:**
- `LogGroup.Phases.Count == 2`
- `Phases[0].Type == PhaseType.Backend`
- `Phases[1].Type == PhaseType.Frontend`
- `Phases[1].StartTime > Phases[0].EndTime`

---

### 4. Sequential Component Loading Detection

**Objective:** Verify detection of sequential (waterfall) component loading

**Setup:**
- Frontend logs with staggered start times (each starts after previous ends)

**Test Data:**
```
Component 1: 21:57:51.235 - 21:57:52.435 (1200ms)
Component 2: 21:57:52.436 - 21:57:53.236 (800ms)
Component 3: 21:57:53.237 - 21:57:53.887 (650ms)
```

**Expected Behavior:**
- ✅ `IsSequentialLoading = true`
- ✅ Parallel savings calculated: (1200 + 800 + 650) - 1200 = 1450ms
- ✅ Recommendation: "Components loading sequentially - optimize for parallel loading to save 1450ms"

**Pass Criteria:**
- `frontendPhase.IsSequentialLoading == true`
- `frontendPhase.ParallelSavings == 1450`

---

### 5. Parallel Component Loading Detection

**Objective:** Verify detection of parallel component loading (desired pattern)

**Setup:**
- Frontend logs with simultaneous start times (all start within 50ms)

**Test Data:**
```
Component 1: 21:57:51.235 - 21:57:52.435 (1200ms)
Component 2: 21:57:51.240 - 21:57:52.040 (800ms)
Component 3: 21:57:51.245 - 21:57:51.895 (650ms)
```

**Expected Behavior:**
- ✅ `IsSequentialLoading = false`
- ✅ No recommendation about sequential loading
- ✅ Total phase duration = longest component (1200ms)

**Pass Criteria:**
- `frontendPhase.IsSequentialLoading == false`
- `frontendPhase.DurationMs <= 1200`

---

### 6. Re-entry Detection (Recursion)

**Objective:** Detect when triggers fire multiple times in one transaction

**Setup:**
- Multiple logs with same trigger name

**Test Data:**
```
apex-07L001.log - CaseTrigger on Case beforeUpdate
apex-07L002.log - Flow: Case_Validation
apex-07L003.log - CaseTrigger on Case afterUpdate (1st re-entry)
apex-07L004.log - @future updateRelatedRecords
apex-07L005.log - CaseTrigger on Case afterUpdate (2nd re-entry)
```

**Expected Behavior:**
- ✅ `ReentryPatterns` contains: `"CaseTrigger" = 3`
- ✅ `TotalReentryCount = 2` (fired 3 times = 2 re-entries)
- ✅ Recommendation: "🔥 CaseTrigger fired 3 times - add recursion control"

**Pass Criteria:**
- `LogGroup.ReentryPatterns.ContainsKey("CaseTrigger")`
- `LogGroup.ReentryPatterns["CaseTrigger"] == 3`
- Recommendation includes recursion control suggestion

---

### 7. Multiple Separate Transactions

**Objective:** Verify unrelated logs are NOT grouped together

**Setup:**
- Folder with logs from different users OR different time windows

**Test Data:**
```
User A:
- apex-07L001.log (21:57:43 - User: victor@loves.com)
- apex-07L002.log (21:57:44 - User: victor@loves.com)

User B:
- apex-07L003.log (21:57:43 - User: contractor@example.com)

Time Gap:
- apex-07L004.log (22:10:00 - User: victor@loves.com) (13 minutes later)
```

**Expected Behavior:**
- ✅ 3 separate `LogGroup` objects created
- ✅ Group 1: User A logs (07L001, 07L002)
- ✅ Group 2: User B log (07L003)
- ✅ Group 3: User A later log (07L004)

**Pass Criteria:**
- `LogGroups.Count == 3`
- Each group has correct user isolation
- Time gaps > 10 seconds create separate groups

---

### 8. Aggregate Metrics Accuracy

**Objective:** Verify metrics are summed correctly across all logs in a group

**Setup:**
- 4 logs with known SOQL/CPU/DML values

**Test Data:**
```
Log 1: 2 SOQL, 50 CPU, 1 DML
Log 2: 5 SOQL, 120 CPU, 0 DML
Log 3: 3 SOQL, 80 CPU, 2 DML
Log 4: 1 SOQL, 45 CPU, 0 DML
```

**Expected Behavior:**
- ✅ `TotalSoqlQueries = 11`
- ✅ `TotalCpuTime = 295`
- ✅ `TotalDmlStatements = 3`
- ✅ Metrics displayed in UI aggregate panel

**Pass Criteria:**
- Sum of all SOQL queries matches
- Sum of all CPU time matches
- Sum of all DML statements matches

---

### 9. Recommendation Generation - Critical Performance

**Objective:** Verify recommendations for slow transactions

**Setup:**
- Transaction group with total duration > 10 seconds

**Expected Behavior:**
- ✅ Recommendation includes: "🔥 Total user wait time: X.X seconds - CRITICAL"
- ✅ Priority fixes listed
- ✅ Severity level indicates critical issue

**Pass Criteria:**
- `TotalDuration > 10000ms` triggers critical warning
- Recommendations list contains specific fixes

---

### 10. Empty Folder Handling

**Objective:** Verify graceful handling of edge cases

**Setup:**
- Load empty folder
- Load folder with no .log files

**Expected Behavior:**
- ✅ Status message: "No log files found in folder"
- ✅ No crash or exception
- ✅ `LogGroups` remains empty

**Pass Criteria:**
- Application remains stable
- User-friendly error message displayed

---

## Testing Checklist

### Functional Testing
- [ ] Single log upload works
- [ ] Folder upload works
- [ ] Transaction grouping by user works
- [ ] Transaction grouping by time works
- [ ] Phase detection (Backend/Frontend) works
- [ ] Sequential loading detection works
- [ ] Parallel loading detection works
- [ ] Re-entry pattern detection works
- [ ] Aggregate metrics calculation correct
- [ ] Recommendations generated appropriately

### Performance Testing
- [ ] Load 10 logs: < 2 seconds
- [ ] Load 50 logs: < 5 seconds
- [ ] Load 100 logs: < 10 seconds
- [ ] Fast metadata extraction (no full parse)

### UI Testing
- [ ] LogGroups display in list
- [ ] Single logs show as "Single Log"
- [ ] Transaction groups show log count
- [ ] Duration display uses correct units (ms vs s)
- [ ] Critical issues highlighted (🔥🔥🔥)
- [ ] Phases display correctly
- [ ] Recommendations display in readable format

### Edge Case Testing
- [ ] Empty folder handling
- [ ] Corrupted log file handling
- [ ] Mixed file types in folder (.log, .txt, .pdf)
- [ ] Very large logs (>10MB)
- [ ] Logs with missing USER_INFO
- [ ] Logs with missing EXECUTION_START/FINISH

---

## Test Data Requirements

To properly test Black Widow, you need:

### Minimum Test Set
1. **1 simple log** - Basic SOQL query (like SAPInventoryCheck example)
2. **3-5 related logs** - Same user, sequential timestamps
3. **Mixed phase logs** - Some backend, some frontend
4. **Recursive trigger logs** - Same trigger multiple times
5. **Sequential component logs** - Waterfall pattern

### Ideal Test Set (Real-World Scenarios)
- Case save with multiple triggers
- Account update with Process Builder
- Lightning page load with 6+ components
- Flow execution with DML operations
- Batch apex execution logs
- @future method chains

---

## Success Criteria

### Must Pass (Blocking Issues)
1. ✅ All logs parse without errors
2. ✅ Grouping logic correctly identifies related logs
3. ✅ Phase detection separates backend/frontend
4. ✅ Aggregate metrics sum correctly
5. ✅ No crashes on edge cases

### Should Pass (Important but not blocking)
1. ✅ Recommendations are accurate and helpful
2. ✅ Sequential loading detection works > 90% of time
3. ✅ Re-entry detection finds all recursion patterns
4. ✅ Performance meets targets (<10s for 100 logs)

### Nice to Have (Future improvements)
1. Record ID extraction accuracy
2. Component name identification
3. N+1 query pattern detection
4. SOQL query optimization suggestions

---

## Reporting Issues

When filing bugs, include:
1. **Log files** (if possible, sanitized data)
2. **Expected behavior** vs **actual behavior**
3. **Screenshots** of the issue
4. **Steps to reproduce**
5. **System info** (Windows version, .NET version)

---

## Marketing Claims We Can Make (After Testing)

Based on functionality implemented, we can claim:

✅ **"The only tool that groups related debug logs into transactions"**  
✅ **"See the complete user journey from button click to page render"**  
✅ **"Detects trigger recursion automatically"**  
✅ **"Identifies sequential component loading and calculates time savings"**  
✅ **"Shows backend vs frontend performance breakdown"**  
✅ **"Aggregate metrics across entire transaction chain"**  
✅ **"Smart recommendations based on detected patterns"**  
✅ **"Explains why your org is slow in plain English"**

---

**Version:** 1.0  
**Last Updated:** February 1, 2026  
**Author:** Black Widow Team
