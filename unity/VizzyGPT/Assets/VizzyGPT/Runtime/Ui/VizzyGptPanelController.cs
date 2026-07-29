#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using ModApi.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VizzyGPT.Core.Api;
using VizzyGPT.Core.Changes;
using VizzyGPT.Core.Conversations;
using VizzyGPT.Core.Diagnostics;
using VizzyGPT.Core.Patching;
using VizzyGPT.Core.Programs;
using VizzyGPT.Core.Validation;
using VizzyGPT.Runtime.Adapters;

namespace VizzyGPT.Runtime.Ui
{
    public sealed class VizzyGptWorkflowEnvironment
    {
        private readonly Func<string, string> programFingerprint;
        private readonly Func<string?> launchProgramXml;
        private string? resolvedLaunchProgramXml;

        public VizzyGptWorkflowEnvironment(
            bool isFlight,
            string programFingerprint,
            string? launchProgramXml,
            Func<string> buildFlightContext,
            Func<string, CancellationToken, Task<PendingChange?>> loadPendingAsync,
            Func<PendingChange, CancellationToken, Task> savePendingAsync,
            Func<string, CancellationToken, Task> deletePendingAsync)
            : this(
                isFlight,
                _ => programFingerprint,
                () => launchProgramXml,
                buildFlightContext,
                loadPendingAsync,
                savePendingAsync,
                deletePendingAsync)
        {
            if (string.IsNullOrWhiteSpace(programFingerprint))
            {
                throw new ArgumentException("Program fingerprint must be non-whitespace.", nameof(programFingerprint));
            }
        }

        public VizzyGptWorkflowEnvironment(
            bool isFlight,
            string programFingerprint,
            Func<string?> launchProgramXml,
            Func<string> buildFlightContext,
            Func<string, CancellationToken, Task<PendingChange?>> loadPendingAsync,
            Func<PendingChange, CancellationToken, Task> savePendingAsync,
            Func<string, CancellationToken, Task> deletePendingAsync)
            : this(
                isFlight,
                _ => programFingerprint,
                launchProgramXml,
                buildFlightContext,
                loadPendingAsync,
                savePendingAsync,
                deletePendingAsync)
        {
            if (string.IsNullOrWhiteSpace(programFingerprint))
            {
                throw new ArgumentException("Program fingerprint must be non-whitespace.", nameof(programFingerprint));
            }
        }

        internal VizzyGptWorkflowEnvironment(
            bool isFlight,
            Func<string, string> programFingerprint,
            Func<string?> launchProgramXml,
            Func<string> buildFlightContext,
            Func<string, CancellationToken, Task<PendingChange?>> loadPendingAsync,
            Func<PendingChange, CancellationToken, Task> savePendingAsync,
            Func<string, CancellationToken, Task> deletePendingAsync)
        {
            IsFlight = isFlight;
            this.programFingerprint = programFingerprint ?? throw new ArgumentNullException(nameof(programFingerprint));
            this.launchProgramXml = launchProgramXml ?? throw new ArgumentNullException(nameof(launchProgramXml));
            BuildFlightContext = buildFlightContext ?? throw new ArgumentNullException(nameof(buildFlightContext));
            LoadPendingAsync = loadPendingAsync ?? throw new ArgumentNullException(nameof(loadPendingAsync));
            SavePendingAsync = savePendingAsync ?? throw new ArgumentNullException(nameof(savePendingAsync));
            DeletePendingAsync = deletePendingAsync ?? throw new ArgumentNullException(nameof(deletePendingAsync));
        }

        public bool IsFlight { get; }
        public Func<string> BuildFlightContext { get; }
        public Func<string, CancellationToken, Task<PendingChange?>> LoadPendingAsync { get; }
        public Func<PendingChange, CancellationToken, Task> SavePendingAsync { get; }
        public Func<string, CancellationToken, Task> DeletePendingAsync { get; }

        public string GetProgramFingerprint(string programHash)
        {
            var value = programFingerprint(programHash);
            return string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException("Program fingerprint resolver returned an empty value.")
                : value;
        }

        public string? ResolveLaunchProgramXml()
        {
            if (!string.IsNullOrWhiteSpace(resolvedLaunchProgramXml))
            {
                return resolvedLaunchProgramXml;
            }

            var value = launchProgramXml();
            if (!string.IsNullOrWhiteSpace(value))
            {
                resolvedLaunchProgramXml = value;
            }

            return value;
        }

        internal static VizzyGptWorkflowEnvironment CreateDefault()
        {
            return new VizzyGptWorkflowEnvironment(
                false,
                hash => "editor-" + hash,
                () => null,
                () => string.Empty,
                (_, __) => Task.FromResult<PendingChange?>(null),
                (_, __) => Task.CompletedTask,
                (_, __) => Task.CompletedTask);
        }
    }

    public enum VizzyGptPanelMode
    {
        Ask,
        Modify
    }

    public enum VizzyGptPanelState
    {
        Closed,
        Idle,
        Sending,
        PreviewReady,
        Applying,
        Error
    }

    public sealed class VizzyGptPanelRenderState
    {
        public VizzyGptPanelRenderState(
            VizzyGptPanelMode mode,
            VizzyGptPanelState state,
            string statusText,
            string transcriptText,
            bool canSend,
            bool canCancel,
            bool canPreview,
            bool canUndo,
            bool canModify)
            : this(
                mode,
                state,
                statusText,
                transcriptText,
                Array.Empty<ConversationEntryRenderModel>(),
                canSend,
                canCancel,
                canPreview,
                canUndo,
                canModify)
        {
        }

        public VizzyGptPanelRenderState(
            VizzyGptPanelMode mode,
            VizzyGptPanelState state,
            string statusText,
            string transcriptText,
            IReadOnlyList<ConversationEntryRenderModel> entries,
            bool canSend,
            bool canCancel,
            bool canPreview,
            bool canUndo,
            bool canModify)
        {
            Mode = mode;
            State = state;
            StatusText = statusText;
            TranscriptText = transcriptText;
            Entries = (entries ?? throw new ArgumentNullException(nameof(entries))).ToArray();
            CanSend = canSend;
            CanCancel = canCancel;
            CanPreview = canPreview;
            CanUndo = canUndo;
            CanModify = canModify;
        }

        public VizzyGptPanelMode Mode { get; }
        public VizzyGptPanelState State { get; }
        public string StatusText { get; }
        public string TranscriptText { get; }
        public IReadOnlyList<ConversationEntryRenderModel> Entries { get; }
        public bool CanSend { get; }
        public bool CanCancel { get; }
        public bool CanPreview { get; }
        public bool CanUndo { get; }
        public bool CanModify { get; }
    }

    public sealed class VizzyGptPanelWorkflow : IDisposable
    {
        private readonly IVizzyRuntimeAdapter adapter;
        private readonly Func<AiRequest, CancellationToken, Task<AiResponse>> sendAsync;
        private readonly Func<string, string, AiRequest> createRequest;
        private readonly Func<string, string, DateTime, CancellationToken, Task> saveBackupAsync;
        private readonly Func<VizzyNodeCatalog> createCatalog;
        private readonly Func<DateTime> utcNow;
        private readonly Action<VizzyGptPanelRenderState> render;
        private readonly VizzyGptWorkflowEnvironment environment;
        private readonly RuntimeCompatibilityResult compatibility;
        private readonly IConversationStore? conversationStore;
        private readonly List<ConversationMessage> messages = new List<ConversationMessage>();
        private readonly List<ConversationStageTiming> activeStages = new List<ConversationStageTiming>();
        private readonly SemaphoreSlim conversationLoadGate = new SemaphoreSlim(1, 1);

        private CancellationTokenSource? requestCancellation;
        private ChangeSession? session;
        private AppliedChange? undoSession;
        private long requestGeneration;
        private bool disposed;
        private bool pendingConflict;
        private string? restoredPendingFingerprint;
        private ConversationHistory? conversationHistory;
        private string? loadedHistoryKey;
        private string? requestedHistoryProgramHash;
        private bool hasRequestedHistory;
        private string? activeEntryId;
        private DateTime requestStartedUtc;
        private DateTime stageStartedUtc;
        private RequestStage? activeStage;

        public VizzyGptPanelWorkflow(
            IVizzyRuntimeAdapter adapter,
            Func<AiRequest, CancellationToken, Task<AiResponse>> sendAsync,
            Func<string, string, AiRequest> createRequest,
            Func<string, string, DateTime, CancellationToken, Task> saveBackupAsync,
            Func<VizzyNodeCatalog> createCatalog,
            Func<DateTime> utcNow,
            Action<VizzyGptPanelRenderState> render,
            VizzyGptWorkflowEnvironment? environment = null,
            RuntimeCompatibilityResult? compatibility = null,
            IConversationStore? conversationStore = null)
        {
            this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            this.sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
            this.createRequest = createRequest ?? throw new ArgumentNullException(nameof(createRequest));
            this.saveBackupAsync = saveBackupAsync ?? throw new ArgumentNullException(nameof(saveBackupAsync));
            this.createCatalog = createCatalog ?? throw new ArgumentNullException(nameof(createCatalog));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.render = render ?? throw new ArgumentNullException(nameof(render));
            this.environment = environment ?? VizzyGptWorkflowEnvironment.CreateDefault();
            this.compatibility = compatibility ??
                RuntimeCompatibilityResult.Compatible("Injected.Vizzy.FlightProgram");
            this.conversationStore = conversationStore;
            RenderState();
        }

        public VizzyGptPanelMode Mode { get; private set; } = VizzyGptPanelMode.Ask;

        public VizzyGptPanelState State { get; private set; } = VizzyGptPanelState.Closed;

        public string StatusText { get; private set; } = string.Empty;

        public string TranscriptText { get; private set; } = string.Empty;

        public bool CanApply =>
            compatibility.CanModify &&
            State == VizzyGptPanelState.PreviewReady &&
            session != null &&
            IsSessionCurrent();

        public bool CanUndo => undoSession != null && State != VizzyGptPanelState.Sending && State != VizzyGptPanelState.Applying;

        public bool HasPendingConflict => pendingConflict;

        public VizzyGptPanelRenderState CurrentRenderState => CreateRenderState();

        public void OpenPanel()
        {
            ThrowIfDisposed();
            if (State == VizzyGptPanelState.Closed)
            {
                Transition(
                    session != null ? VizzyGptPanelState.PreviewReady : VizzyGptPanelState.Idle,
                    !compatibility.CanModify
                        ? compatibility.Diagnostic
                        : session != null
                            ? "Pending flight preview ready."
                            : "Ready.");
            }
        }

        public void ClosePanel()
        {
            if (disposed)
            {
                return;
            }

            CancelRequest();
            var finalizedActiveRequest = FinalizeActiveRequestAsCancelled();
            requestGeneration++;
            if (restoredPendingFingerprint == null)
            {
                session = null;
            }
            Transition(VizzyGptPanelState.Closed, string.Empty);
            if (finalizedActiveRequest)
            {
                _ = PersistClosedConversationAsync();
            }
        }

        public void SetMode(VizzyGptPanelMode mode)
        {
            ThrowIfDisposed();
            if (!Enum.IsDefined(typeof(VizzyGptPanelMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            if (mode == VizzyGptPanelMode.Modify && !compatibility.CanModify)
            {
                Mode = VizzyGptPanelMode.Ask;
                session = null;
                Transition(VizzyGptPanelState.Idle, compatibility.Diagnostic);
                return;
            }

            if (State == VizzyGptPanelState.Sending || State == VizzyGptPanelState.Applying)
            {
                return;
            }

            if (Mode == mode)
            {
                RenderState();
                return;
            }

            Mode = mode;
            session = null;
            if (State != VizzyGptPanelState.Closed)
            {
                Transition(VizzyGptPanelState.Idle, mode == VizzyGptPanelMode.Ask ? "Ask mode." : "Modify mode.");
            }
            _ = LoadConversationAsync();
        }

        public async Task SendPromptAsync(string prompt)
        {
            ThrowIfDisposed();
            if (State == VizzyGptPanelState.Closed)
            {
                Transition(VizzyGptPanelState.Error, "Open the panel before sending a request.");
                return;
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                Transition(VizzyGptPanelState.Error, "A prompt is required.");
                return;
            }

            CancelRequest();
            FinalizeActiveRequestAsCancelled();
            var generation = ++requestGeneration;
            session = null;
            pendingConflict = false;
            var cancellation = new CancellationTokenSource();
            requestCancellation = cancellation;
            var cancellationToken = cancellation.Token;
            Transition(VizzyGptPanelState.Sending, "Sending request.");
            BeginRequestEntries(prompt);

            try
            {
                AdvanceStage(RequestStage.ReadingProgram);
                var requestContext = BuildRequestContext();
                AdvanceStage(RequestStage.BuildingContext);
                await EnsureConversationLoadedAsync(requestContext.SourceHash, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentRequest(generation, cancellation))
                {
                    return;
                }

                if (requestContext.Mode == VizzyGptPanelMode.Ask)
                {
                    AdvanceStage(RequestStage.WaitingForModel);
                    var response = await sendAsync(createRequest(prompt, requestContext.AiContext), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsCurrentRequest(generation, cancellation))
                    {
                        return;
                    }

                    session = null;
                    CompleteAssistantEntry(response, false);
                    Transition(VizzyGptPanelState.Idle, "Response received.");
                    await PersistConversationAsync();
                    return;
                }

                var attempt = await RunModifyAttemptAsync(prompt, requestContext, null, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentRequest(generation, cancellation))
                {
                    return;
                }

                if (attempt.Failure != null && attempt.Failure.IsRepairable)
                {
                    AdvanceStage(RequestStage.RepairingPatch);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsCurrentRequest(generation, cancellation))
                    {
                        return;
                    }

                    var repairContext = BuildRepairContext(attempt.Failure, requestContext.SourceHash!);
                    attempt = await RunModifyAttemptAsync(
                        prompt,
                        requestContext,
                        repairContext,
                        cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentRequest(generation, cancellation))
                {
                    return;
                }

                if (attempt.Failure != null || attempt.Session == null || attempt.Response == null)
                {
                    session = null;
                    CompleteErrorEntry(attempt.Failure ?? new ModifyValidationFailure(
                        "ModifyFailure",
                        null,
                        "The response did not contain an applicable change.",
                        "Modify attempt completed without a preview session.",
                        false));
                    Transition(VizzyGptPanelState.Error, attempt.Failure?.Message ?? "The response did not contain an applicable change.");
                    await PersistConversationAsync();
                    return;
                }

                AdvanceStage(RequestStage.PreparingPreview);
                session = attempt.Session;
                CompleteAssistantEntry(attempt.Response, true);
                if (environment.IsFlight)
                {
                    var fingerprint = environment.GetProgramFingerprint(requestContext.SourceHash!);
                    var existing = await environment.LoadPendingAsync(fingerprint, cancellationToken);
                    Transition(
                        VizzyGptPanelState.PreviewReady,
                        existing == null
                            ? "Preview ready. Apply will save this change for Vizzy."
                            : "Preview ready. Apply will replace the existing pending change.");
                }
                else
                {
                    Transition(VizzyGptPanelState.PreviewReady, "Preview ready.");
                }

                await PersistConversationAsync();
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentRequest(generation, cancellation))
                {
                    CompleteCancelledEntry();
                    Transition(VizzyGptPanelState.Idle, "Request cancelled.");
                    await PersistConversationAsync();
                }
            }
            catch (Exception exception)
            {
                if (IsCurrentRequest(generation, cancellation))
                {
                    session = null;
                    var diagnostic = ExceptionDiagnostic.From(
                        exception,
                        activeStage?.ToString() ?? "Request");
                    CompleteErrorEntry(new ModifyValidationFailure(
                        diagnostic.Code,
                        null,
                        diagnostic.DisplayMessage,
                        diagnostic.TechnicalDetails,
                        false));
                    Transition(VizzyGptPanelState.Error, diagnostic.DisplayMessage);
                    await PersistConversationAsync();
                }
            }
            finally
            {
                cancellation.Dispose();
                if (ReferenceEquals(requestCancellation, cancellation))
                {
                    requestCancellation = null;
                }
            }
        }

        public void CancelRequest()
        {
            requestCancellation?.Cancel();
        }

        public void RefreshElapsed()
        {
            ThrowIfDisposed();
            if (State == VizzyGptPanelState.Sending && activeEntryId != null)
            {
                RenderState();
            }
        }

        public async Task LoadConversationAsync()
        {
            ThrowIfDisposed();
            string? programHash = null;
            if (Mode == VizzyGptPanelMode.Modify &&
                TryReadCurrent(out _, out var currentHash, out _))
            {
                programHash = currentHash;
            }

            await EnsureConversationLoadedAsync(programHash, CancellationToken.None);
        }

        public async Task RestorePendingAsync()
        {
            ThrowIfDisposed();
            if (environment.IsFlight)
            {
                return;
            }

            if (!compatibility.CanModify)
            {
                Transition(VizzyGptPanelState.Idle, compatibility.Diagnostic);
                return;
            }

            if (!adapter.TryGetEditorProgramXml(out var xml, out var readError))
            {
                Transition(VizzyGptPanelState.Error, readError);
                return;
            }

            var current = VizzyProgramDocument.Parse(xml);
            var currentHash = VizzyProgramHash.Compute(current);
            var fingerprint = environment.GetProgramFingerprint(currentHash);
            var pending = await environment.LoadPendingAsync(fingerprint, CancellationToken.None);
            if (pending == null)
            {
                return;
            }

            await EnsureConversationLoadedAsync(pending.BaseHash, CancellationToken.None);
            Mode = VizzyGptPanelMode.Modify;
            restoredPendingFingerprint = pending.ProgramFingerprint;
            var rebase = new PendingChangeRebaser().TryRebase(pending, current);
            if (rebase.Status == RebaseStatus.Conflict || rebase.Patch == null)
            {
                session = null;
                pendingConflict = true;
                Transition(
                    VizzyGptPanelState.Error,
                    "Pending change conflict. Send a new Modify prompt or Cancel to discard it.");
                return;
            }

            try
            {
                var result = VizzyPatchEngine.Apply(current, rebase.Patch);
                var report = new VizzyProgramValidator(adapter.ValidateWithProgramSerializer)
                    .Validate(result.Document, createCatalog());
                if (!report.IsValid)
                {
                    throw new InvalidOperationException(report.Errors[0].Message);
                }

                session = ChangeSession.Create(current, rebase.Patch, result, report);
                pendingConflict = false;
                Transition(
                    VizzyGptPanelState.PreviewReady,
                    rebase.Status == RebaseStatus.Rebased
                        ? "Pending flight change rebased and ready for preview."
                        : "Pending flight change ready for preview.");
            }
            catch (Exception exception)
            {
                session = null;
                pendingConflict = true;
                Transition(VizzyGptPanelState.Error, "Pending change conflict: " + exception.Message);
            }
        }

        public async Task DiscardPendingAsync()
        {
            ThrowIfDisposed();
            if (restoredPendingFingerprint == null)
            {
                return;
            }

            await environment.DeletePendingAsync(restoredPendingFingerprint, CancellationToken.None);
            restoredPendingFingerprint = null;
            pendingConflict = false;
            session = null;
            Transition(VizzyGptPanelState.Idle, "Pending change discarded.");
        }

        public PreviewDialogModel? ShowPreview()
        {
            ThrowIfDisposed();
            if (session == null || !IsSessionCurrent())
            {
                if (session != null)
                {
                    Transition(VizzyGptPanelState.Error, "The Vizzy program changed after the preview was created.");
                }

                return null;
            }

            return PreviewDialogModel.FromSession(session);
        }

        public async Task<bool> ApplySessionAsync()
        {
            ThrowIfDisposed();
            if (session == null || State != VizzyGptPanelState.PreviewReady)
            {
                return false;
            }

            if (environment.IsFlight)
            {
                if (!IsSessionCurrent())
                {
                    Transition(VizzyGptPanelState.Error, "The flight program changed after the preview was created.");
                    return false;
                }

                var pending = PendingChange.Create(
                    environment.GetProgramFingerprint(session.BaseHash),
                    session,
                    utcNow());
                await environment.SavePendingAsync(pending, CancellationToken.None);
                session = null;
                Transition(VizzyGptPanelState.Idle, "Pending change saved. Return to Vizzy to apply it.");
                await PersistConversationAsync();
                return true;
            }

            if (!TryReadCurrent(out var currentXml, out var currentHash, out var error) ||
                !string.Equals(currentHash, session.BaseHash, StringComparison.Ordinal))
            {
                Transition(VizzyGptPanelState.Error, error ?? "The Vizzy program changed after the preview was created.");
                return false;
            }

            Transition(VizzyGptPanelState.Applying, "Saving backup.");
            try
            {
                await saveBackupAsync(ProgramFingerprint(session.BaseHash), currentXml, utcNow(), CancellationToken.None);
            }
            catch (Exception exception)
            {
                Transition(VizzyGptPanelState.Error, "Backup failed: " + exception.Message);
                return false;
            }

            if (!adapter.TrySetEditorProgramXml(session.ResultXml, out var setError))
            {
                Transition(VizzyGptPanelState.Error, "Apply failed: " + setError);
                return false;
            }

            undoSession = new AppliedChange(session, currentXml);
            var resultHash = session.ResultHash;
            session = null;
            if (restoredPendingFingerprint != null)
            {
                await environment.DeletePendingAsync(restoredPendingFingerprint, CancellationToken.None);
                restoredPendingFingerprint = null;
            }
            Transition(VizzyGptPanelState.Idle, "Changes applied.");
            await LinkConversationHashAsync(resultHash);
            await PersistConversationAsync();
            return true;
        }

        public async Task<bool> UndoLastAsync()
        {
            ThrowIfDisposed();
            if (undoSession == null)
            {
                return false;
            }

            var applied = undoSession;
            var serializerIssue = adapter.ValidateWithProgramSerializer(applied.ExactBaseXml);
            if (serializerIssue != null)
            {
                Transition(VizzyGptPanelState.Error, serializerIssue.Message);
                return false;
            }

            if (!TryReadCurrent(out var currentXml, out var currentHash, out var error))
            {
                Transition(VizzyGptPanelState.Error, error ?? "Unable to read the current Vizzy program.");
                return false;
            }

            if (!string.Equals(currentHash, applied.Session.ResultHash, StringComparison.Ordinal))
            {
                Transition(VizzyGptPanelState.Error, "The Vizzy program changed after GPT applied the preview.");
                return false;
            }

            Transition(VizzyGptPanelState.Applying, "Saving undo backup.");
            try
            {
                await saveBackupAsync(ProgramFingerprint(applied.Session.ResultHash), currentXml, utcNow(), CancellationToken.None);
            }
            catch (Exception exception)
            {
                Transition(VizzyGptPanelState.Error, "Undo backup failed: " + exception.Message);
                return false;
            }

            if (!adapter.TrySetEditorProgramXml(applied.ExactBaseXml, out var setError))
            {
                Transition(VizzyGptPanelState.Error, "Undo failed: " + setError);
                return false;
            }

            undoSession = null;
            Transition(VizzyGptPanelState.Idle, "Changes restored.");
            await LinkConversationHashAsync(applied.Session.BaseHash);
            await PersistConversationAsync();
            return true;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            ClosePanel();
            requestCancellation?.Dispose();
            requestCancellation = null;
            disposed = true;
        }

        private RequestContext BuildRequestContext()
        {
            if (Mode == VizzyGptPanelMode.Ask)
            {
                var context = BuildReadOnlyProgramContext();
                if (environment.IsFlight)
                {
                    context += "\n" + environment.BuildFlightContext();
                }

                return new RequestContext(
                    context + "\nAsk mode does not permit program mutation.",
                    VizzyGptPanelMode.Ask,
                    null,
                    null,
                    null);
            }

            var read = environment.IsFlight
                ? TryGetFlightSnapshot(out var xml, out var error)
                : adapter.TryGetEditorProgramXml(out xml, out error);
            if (!read)
            {
                throw new InvalidOperationException(error);
            }

            var document = VizzyProgramDocument.Parse(xml);
            var sourceReport = new VizzyProgramValidator(adapter.ValidateWithProgramSerializer)
                .Validate(document, createCatalog());
            if (!sourceReport.IsValid)
            {
                throw new InvalidOperationException(
                    "Current Vizzy program is not safe to modify: " + sourceReport.Errors[0].Message);
            }

            var baseHash = VizzyProgramHash.Compute(document);
            return new RequestContext(
                "Program base hash:\n" + baseHash + "\n" +
                new ContextBuilder().BuildEditorContext(document, string.Empty, null) +
                (environment.IsFlight ? "\n" + environment.BuildFlightContext() : string.Empty),
                VizzyGptPanelMode.Modify,
                xml,
                document,
                baseHash);
        }

        private string BuildReadOnlyProgramContext()
        {
            var read = environment.IsFlight
                ? TryGetFlightSnapshot(out var xml, out _)
                : adapter.TryGetEditorProgramXml(out xml, out _);
            if (!read || string.IsNullOrWhiteSpace(xml))
            {
                return environment.IsFlight ? "FLIGHT PROGRAM CONTEXT\nUnavailable." : "EDITOR CONTEXT\nUnavailable.";
            }

            try
            {
                return new ContextBuilder().BuildEditorContext(
                    VizzyProgramDocument.Parse(xml),
                    string.Empty,
                    null);
            }
            catch (Exception)
            {
                return environment.IsFlight ? "FLIGHT PROGRAM CONTEXT\nUnavailable." : "EDITOR CONTEXT\nUnavailable.";
            }
        }

        private async Task<ModifyAttemptResult> RunModifyAttemptAsync(
            string prompt,
            RequestContext source,
            string? repairContext,
            CancellationToken cancellationToken)
        {
            AdvanceStage(RequestStage.WaitingForModel);
            var context = repairContext == null
                ? source.AiContext
                : source.AiContext + "\n\n" + repairContext;
            var response = await sendAsync(createRequest(prompt, context), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            AdvanceStage(RequestStage.ParsingPatch);

            if (!response.CanApply || response.Patch == null)
            {
                var details = response.Diagnostics.Count == 0
                    ? "The model response did not include a patch."
                    : string.Join("; ", response.Diagnostics);
                return ModifyAttemptResult.Failed(
                    response,
                    new ModifyValidationFailure(
                        "PatchContract",
                        null,
                        "The response did not contain an applicable change.",
                        SanitizeTechnicalDetails(details, RequestStage.ParsingPatch),
                        true));
            }

            if (source.SourceDocument == null || source.SourceHash == null)
            {
                return ModifyAttemptResult.Failed(
                    response,
                    new ModifyValidationFailure(
                        "MissingSource",
                        null,
                        "Modify mode did not capture an editor program.",
                        "The captured Modify source document or hash was null.",
                        false));
            }

            if (!TryReadCurrent(out _, out var currentHash, out var readError))
            {
                var safeReadError = SanitizeTechnicalDetails(
                    readError ?? "Unable to read the current Vizzy program.",
                    RequestStage.ValidatingPatch);
                return ModifyAttemptResult.Failed(
                    response,
                    new ModifyValidationFailure(
                        "SourceUnavailable",
                        null,
                        safeReadError,
                        safeReadError,
                        false));
            }

            if (!string.Equals(currentHash, source.SourceHash, StringComparison.Ordinal))
            {
                return ModifyAttemptResult.Failed(
                    response,
                    new ModifyValidationFailure(
                        "StaleSource",
                        null,
                        "The Vizzy program changed while the request was in progress.",
                        "The current program hash no longer matches the captured request source hash.",
                        false));
            }

            AdvanceStage(RequestStage.ValidatingPatch);
            PatchResult result;
            try
            {
                result = VizzyPatchEngine.Apply(source.SourceDocument, response.Patch);
            }
            catch (PatchApplyException exception)
            {
                var diagnostic = ExceptionDiagnostic.From(exception, RequestStage.ValidatingPatch.ToString());
                var code = diagnostic.DisplayMessage.IndexOf("protected", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "ProtectedRoot"
                    : diagnostic.Code;
                var path = response.Patch.Operations
                    .Select(operation => operation.Target?.Path)
                    .FirstOrDefault(value => value != null);
                return ModifyAttemptResult.Failed(
                    response,
                    new ModifyValidationFailure(
                        code,
                        path,
                        diagnostic.DisplayMessage,
                        diagnostic.TechnicalDetails,
                        true));
            }

            var report = new VizzyProgramValidator(adapter.ValidateWithProgramSerializer)
                .Validate(result.Document, createCatalog());
            if (!report.IsValid)
            {
                var issue = report.Errors[0];
                var safeMessage = SanitizeTechnicalDetails(
                    issue.Message,
                    RequestStage.ValidatingPatch);
                return ModifyAttemptResult.Failed(
                    response,
                    new ModifyValidationFailure(
                        issue.Code,
                        issue.Path,
                        safeMessage,
                        safeMessage,
                        true));
            }

            return ModifyAttemptResult.Succeeded(
                response,
                ChangeSession.Create(source.SourceDocument, response.Patch, result, report));
        }

        private static string BuildRepairContext(ModifyValidationFailure failure, string originalHash)
        {
            return "MODIFY REPAIR\n" +
                "The previous patch could not be safely previewed.\n" +
                "Error code: " + failure.Code + "\n" +
                "Path: " + (failure.Path ?? "root") + "\n" +
                "Error: " + SanitizeTechnicalDetails(failure.Message, RequestStage.RepairingPatch) + "\n" +
                "Return a complete replacement patch against original base hash " + originalHash + ".\n" +
                "Do not add, remove, replace, or move direct Program structural containers.";
        }

        private static string SanitizeTechnicalDetails(string value, RequestStage stage)
        {
            return ExceptionDiagnostic.From(new InvalidOperationException(value), stage.ToString()).DisplayMessage;
        }

        private void BeginRequestEntries(string prompt)
        {
            requestStartedUtc = utcNow();
            stageStartedUtc = requestStartedUtc;
            activeStage = null;
            activeStages.Clear();
            var mode = ToConversationMode(Mode);
            messages.Add(new ConversationMessage(
                Guid.NewGuid().ToString("N"),
                ConversationRole.User,
                ConversationMessageKind.Message,
                mode,
                prompt,
                null,
                Array.Empty<ConversationStageTiming>(),
                null,
                null,
                requestStartedUtc));
            activeEntryId = Guid.NewGuid().ToString("N");
            messages.Add(new ConversationMessage(
                activeEntryId,
                ConversationRole.Assistant,
                ConversationMessageKind.Progress,
                mode,
                string.Empty,
                null,
                Array.Empty<ConversationStageTiming>(),
                0,
                null,
                requestStartedUtc));
            RenderState();
        }

        private void AdvanceStage(RequestStage stage)
        {
            var now = utcNow();
            if (activeStage.HasValue)
            {
                activeStages.Add(new ConversationStageTiming(
                    activeStage.Value.ToString(),
                    Math.Max(0, (now - stageStartedUtc).TotalSeconds)));
            }

            activeStage = stage;
            stageStartedUtc = now;
            RenderState();
        }

        private void CompleteAssistantEntry(AiResponse response, bool canPreview)
        {
            CompleteActiveStage();
            ReplaceActiveEntry(new ConversationMessage(
                activeEntryId!,
                ConversationRole.Assistant,
                ConversationMessageKind.Message,
                ToConversationMode(Mode),
                response.Message,
                response.Metadata.ReasoningSummary,
                activeStages.ToArray(),
                ElapsedSinceRequest(),
                null,
                requestStartedUtc));
            AppendTranscript(response.Message);
        }

        private void CompleteErrorEntry(ModifyValidationFailure failure)
        {
            CompleteActiveStage();
            var stage = activeStage?.ToString() ?? "Request";
            var error = new ConversationError(
                failure.Code,
                stage,
                failure.Message,
                failure.TechnicalDetails,
                failure.Path);
            ReplaceActiveEntry(new ConversationMessage(
                activeEntryId ?? Guid.NewGuid().ToString("N"),
                ConversationRole.Assistant,
                ConversationMessageKind.Error,
                ToConversationMode(Mode),
                failure.Message,
                null,
                activeStages.ToArray(),
                ElapsedSinceRequest(),
                error,
                requestStartedUtc == default ? utcNow() : requestStartedUtc));
        }

        private void CompleteCancelledEntry()
        {
            CompleteActiveStage();
            ReplaceActiveEntry(new ConversationMessage(
                activeEntryId!,
                ConversationRole.Assistant,
                ConversationMessageKind.Cancelled,
                ToConversationMode(Mode),
                "Request cancelled.",
                null,
                activeStages.ToArray(),
                ElapsedSinceRequest(),
                null,
                requestStartedUtc));
        }

        private bool FinalizeActiveRequestAsCancelled()
        {
            if (activeEntryId == null)
            {
                return false;
            }

            CompleteCancelledEntry();
            return true;
        }

        private void CompleteActiveStage()
        {
            if (!activeStage.HasValue)
            {
                return;
            }

            var now = utcNow();
            activeStages.Add(new ConversationStageTiming(
                activeStage.Value.ToString(),
                Math.Max(0, (now - stageStartedUtc).TotalSeconds)));
            stageStartedUtc = now;
        }

        private void ReplaceActiveEntry(ConversationMessage replacement)
        {
            var index = activeEntryId == null
                ? -1
                : messages.FindIndex(message => string.Equals(message.Id, activeEntryId, StringComparison.Ordinal));
            if (index >= 0)
            {
                messages[index] = replacement;
            }
            else
            {
                messages.Add(replacement);
            }

            activeEntryId = null;
            activeStage = null;
            RenderState();
        }

        private double ElapsedSinceRequest()
        {
            return Math.Max(0, (utcNow() - requestStartedUtc).TotalSeconds);
        }

        private async Task EnsureConversationLoadedAsync(string? programHash, CancellationToken cancellationToken)
        {
            if (conversationStore == null)
            {
                return;
            }

            await conversationLoadGate.WaitAsync(cancellationToken);
            try
            {
                var key = programHash ?? "<general>";
                requestedHistoryProgramHash = programHash;
                hasRequestedHistory = true;
                if (string.Equals(loadedHistoryKey, key, StringComparison.Ordinal))
                {
                    return;
                }

                try
                {
                    conversationHistory = await conversationStore.LoadOrCreateAsync(programHash, cancellationToken);
                    if (disposed)
                    {
                        return;
                    }
                    var currentTurn = CaptureActiveTurn();
                    messages.Clear();
                    messages.AddRange(conversationHistory.Messages);
                    messages.AddRange(currentTurn);
                    loadedHistoryKey = key;
                    RenderState();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    conversationHistory = null;
                    loadedHistoryKey = null;
                    AddPersistenceWarning(exception, "ConversationLoad");
                }
            }
            finally
            {
                conversationLoadGate.Release();
            }
        }

        private ConversationMessage[] CaptureActiveTurn()
        {
            if (activeEntryId == null)
            {
                return Array.Empty<ConversationMessage>();
            }

            var progressIndex = messages.FindIndex(
                message => string.Equals(message.Id, activeEntryId, StringComparison.Ordinal));
            if (progressIndex < 0)
            {
                return Array.Empty<ConversationMessage>();
            }

            var startIndex = progressIndex > 0 &&
                messages[progressIndex - 1].Role == ConversationRole.User
                ? progressIndex - 1
                : progressIndex;
            return messages.Skip(startIndex).ToArray();
        }

        private async Task PersistConversationAsync()
        {
            if (conversationStore == null)
            {
                return;
            }

            try
            {
                if (conversationHistory == null)
                {
                    conversationHistory = await conversationStore.LoadOrCreateAsync(
                        hasRequestedHistory ? requestedHistoryProgramHash : null,
                        CancellationToken.None);
                }

                conversationHistory = new ConversationHistory(
                    conversationHistory.SchemaVersion,
                    conversationHistory.ConversationId,
                    messages.ToArray());
                await conversationStore.SaveAsync(conversationHistory, CancellationToken.None);
            }
            catch (Exception exception)
            {
                AddPersistenceWarning(exception, "ConversationSave");
            }
        }

        private async Task PersistClosedConversationAsync()
        {
            try
            {
                await PersistConversationAsync();
            }
            catch
            {
                // Close and Dispose cannot await persistence or propagate lifecycle callback failures.
            }
        }

        private async Task LinkConversationHashAsync(string programHash)
        {
            if (conversationStore == null || conversationHistory == null)
            {
                return;
            }

            try
            {
                await conversationStore.LinkProgramHashAsync(
                    conversationHistory.ConversationId,
                    programHash,
                    CancellationToken.None);
                loadedHistoryKey = programHash;
                requestedHistoryProgramHash = programHash;
                hasRequestedHistory = true;
            }
            catch (Exception exception)
            {
                AddPersistenceWarning(exception, "ConversationLink");
            }
        }

        private void AddPersistenceWarning(Exception exception, string stage)
        {
            var diagnostic = ExceptionDiagnostic.From(exception, stage);
            var warning = new ConversationError(
                "PersistenceWarning",
                stage,
                "Conversation history could not be persisted.",
                diagnostic.TechnicalDetails,
                null);
            messages.Add(new ConversationMessage(
                Guid.NewGuid().ToString("N"),
                ConversationRole.System,
                ConversationMessageKind.Error,
                ToConversationMode(Mode),
                warning.Summary,
                null,
                Array.Empty<ConversationStageTiming>(),
                null,
                warning,
                utcNow()));
            if (!disposed)
            {
                RenderState();
            }
        }

        private static ConversationMode ToConversationMode(VizzyGptPanelMode mode)
        {
            return mode == VizzyGptPanelMode.Modify ? ConversationMode.Modify : ConversationMode.Ask;
        }

        private bool IsSessionCurrent()
        {
            if (session == null)
            {
                return false;
            }

            return TryReadCurrent(out _, out var hash, out _) &&
                string.Equals(hash, session.BaseHash, StringComparison.Ordinal);
        }

        private bool TryReadCurrent(out string xml, out string hash, out string? error)
        {
            xml = string.Empty;
            hash = string.Empty;
            error = null;
            var read = environment.IsFlight
                ? TryGetFlightSnapshot(out xml, out var readError)
                : adapter.TryGetEditorProgramXml(out xml, out readError);
            if (!read)
            {
                error = readError;
                return false;
            }

            try
            {
                hash = VizzyProgramHash.Compute(VizzyProgramDocument.Parse(xml));
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private void AppendTranscript(string value)
        {
            var escaped = SecurityElement.Escape(value) ?? string.Empty;
            TranscriptText = TranscriptText.Length == 0 ? escaped : TranscriptText + "\n" + escaped;
            RenderState();
        }

        private void Transition(VizzyGptPanelState state, string status)
        {
            State = state;
            StatusText = SecurityElement.Escape(status) ?? string.Empty;
            RenderState();
        }

        private void RenderState()
        {
            render(CreateRenderState());
        }

        private VizzyGptPanelRenderState CreateRenderState()
        {
            var isBusy = State == VizzyGptPanelState.Sending || State == VizzyGptPanelState.Applying;
            return new VizzyGptPanelRenderState(
                Mode,
                State,
                StatusText,
                TranscriptText,
                CreateEntryRenderModels(),
                State != VizzyGptPanelState.Closed && !isBusy,
                State == VizzyGptPanelState.Sending || pendingConflict,
                CanApply,
                CanUndo,
                compatibility.CanModify);
        }

        private IReadOnlyList<ConversationEntryRenderModel> CreateEntryRenderModels()
        {
            var previewMessageId = session != null && State == VizzyGptPanelState.PreviewReady
                ? messages.LastOrDefault(message =>
                    message.Role == ConversationRole.Assistant &&
                    message.Kind == ConversationMessageKind.Message)?.Id
                : null;
            return messages.Select(message =>
            {
                var isActive = activeEntryId != null &&
                    string.Equals(message.Id, activeEntryId, StringComparison.Ordinal);
                return new ConversationEntryRenderModel(
                    message.Id,
                    message.Role,
                    message.Text,
                    message.ReasoningSummary,
                    isActive ? activeStages.ToArray() : message.Stages,
                    isActive ? activeStage : null,
                    isActive ? ElapsedSinceRequest() : message.ElapsedSeconds,
                    message.Error,
                    !isActive &&
                        string.Equals(message.Id, previewMessageId, StringComparison.Ordinal));
            }).ToArray();
        }

        private string ProgramFingerprint(string hash)
        {
            return environment.GetProgramFingerprint(hash);
        }

        private bool TryGetFlightSnapshot(out string xml, out string error)
        {
            xml = environment.ResolveLaunchProgramXml() ?? string.Empty;
            error = string.Empty;
            if (!string.IsNullOrWhiteSpace(xml))
            {
                return true;
            }

            error = "The launch-time flight program snapshot is unavailable.";
            return false;
        }

        private bool IsCurrentRequest(long generation, CancellationTokenSource cancellation)
        {
            return !disposed && State != VizzyGptPanelState.Closed &&
                generation == requestGeneration && ReferenceEquals(requestCancellation, cancellation);
        }

        private sealed class AppliedChange
        {
            public AppliedChange(ChangeSession session, string exactBaseXml)
            {
                Session = session ?? throw new ArgumentNullException(nameof(session));
                ExactBaseXml = exactBaseXml ?? throw new ArgumentNullException(nameof(exactBaseXml));
            }

            public ChangeSession Session { get; }

            public string ExactBaseXml { get; }
        }

        private sealed class ModifyAttemptResult
        {
            private ModifyAttemptResult(
                AiResponse response,
                ChangeSession? session,
                ModifyValidationFailure? failure)
            {
                Response = response;
                Session = session;
                Failure = failure;
            }

            public AiResponse Response { get; }
            public ChangeSession? Session { get; }
            public ModifyValidationFailure? Failure { get; }

            public static ModifyAttemptResult Succeeded(AiResponse response, ChangeSession session)
            {
                return new ModifyAttemptResult(response, session, null);
            }

            public static ModifyAttemptResult Failed(AiResponse response, ModifyValidationFailure failure)
            {
                return new ModifyAttemptResult(response, null, failure);
            }
        }

        private sealed class RequestContext
        {
            public RequestContext(
                string aiContext,
                VizzyGptPanelMode mode,
                string? sourceXml,
                VizzyProgramDocument? sourceDocument,
                string? sourceHash)
            {
                AiContext = aiContext ?? throw new ArgumentNullException(nameof(aiContext));
                Mode = mode;
                SourceXml = sourceXml;
                SourceDocument = sourceDocument;
                SourceHash = sourceHash;
            }

            public string AiContext { get; }

            public VizzyGptPanelMode Mode { get; }

            // This is retained with the parsed document/hash so the request always has one exact source snapshot.
            public string? SourceXml { get; }

            public VizzyProgramDocument? SourceDocument { get; }

            public string? SourceHash { get; }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(VizzyGptPanelWorkflow));
            }
        }
    }

    public sealed class VizzyGptPanelController : MonoBehaviour
    {
        private VizzyGptPanelWorkflow? workflow;
        private string prompt = string.Empty;
        private Action? openSettings;
        private Action<PreviewDialogModel>? openPreview;
        private TMP_InputField? composerInput;
        private TMP_FontAsset? expectedCjkFont;
        private ConversationMessageListView? messageList;
        private RectTransform? panelRoot;
        private Button? launcherButton;
        private Button? sendButton;
        private Button? cancelButton;
        private Button? undoButton;
        private Toggle? askToggle;
        private Toggle? modifyToggle;
        private float nextElapsedRefreshTime;

        public VizzyGptPanelWorkflow Workflow => workflow ??
            throw new InvalidOperationException("Vizzy GPT panel is not configured.");

        public void Configure(
            VizzyGptPanelWorkflow value,
            Action? openSettings = null,
            Action<PreviewDialogModel>? openPreview = null)
        {
            workflow?.Dispose();
            workflow = value ?? throw new ArgumentNullException(nameof(value));
            this.openSettings = openSettings;
            this.openPreview = openPreview;
            Render(workflow.CurrentRenderState);
            _ = workflow.LoadConversationAsync();
        }

        public void Bind(
            IXmlLayout layout,
            TMP_FontAsset? cjkFont = null,
            Func<GameObject, TMP_Text>? createDynamicText = null)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            UnbindInput();
            messageList?.Dispose();
            composerInput = RequireElement<TMP_InputField>(layout, "composer-input");
            var conversationScroll = RequireElement<ScrollRect>(layout, "conversation-scroll");
            var conversationContent = RequireElement<RectTransform>(layout, "conversation-content");
            panelRoot = RequireElement<RectTransform>(layout, "vizzy-gpt-panel");
            var modePanel = RequireElement<RectTransform>(layout, "mode-panel");
            ConstrainPanelToParent(panelRoot);
            ConstrainPanelChildWidth(panelRoot, (RectTransform)composerInput.transform);
            ConstrainPanelChildWidth(panelRoot, modePanel);
            launcherButton = RequireElement<Button>(layout, "gpt-launcher-button");
            sendButton = RequireElement<Button>(layout, "send-button");
            cancelButton = RequireElement<Button>(layout, "cancel-request-button");
            undoButton = RequireElement<Button>(layout, "undo-button");
            askToggle = RequireElement<Toggle>(layout, "ask-toggle");
            modifyToggle = RequireElement<Toggle>(layout, "modify-toggle");

            expectedCjkFont = cjkFont;
            CjkTextFontApplicator.ApplyToInput(composerInput, cjkFont);
            messageList = new ConversationMessageListView(
                conversationContent,
                conversationScroll,
                (RectTransform)composerInput.transform,
                cjkFont,
                OnPreviewButtonClicked,
                createDynamicText);

            if (composerInput != null)
            {
                // The stock TMP input owns focus; ModApi exposes its UI focus gates as read-only.
                composerInput.onValueChanged.AddListener(OnPromptValueChanged);
                composerInput.text = prompt;
            }

            Render(workflow?.CurrentRenderState);
        }

        public void SetPromptText(string value)
        {
            prompt = value ?? string.Empty;
        }

        public void RefreshDynamicTextFonts()
        {
            var inputChanged = CjkTextFontApplicator.ApplyToInput(composerInput, expectedCjkFont);
            messageList?.RefreshDynamicTextFonts();
            if (inputChanged)
            {
                composerInput?.ForceLabelUpdate();
            }
        }

        public void OnOpenPanelButtonClicked()
        {
            workflow?.OpenPanel();
        }

        public void OnClosePanelButtonClicked()
        {
            workflow?.ClosePanel();
        }

        public void OnAskModeClicked()
        {
            workflow?.SetMode(VizzyGptPanelMode.Ask);
        }

        public void OnModifyModeClicked()
        {
            workflow?.SetMode(VizzyGptPanelMode.Modify);
        }

        public async void OnSendButtonClicked()
        {
            if (workflow == null)
            {
                return;
            }

            var entryCount = workflow.CurrentRenderState.Entries.Count;
            var send = workflow.SendPromptAsync(prompt);
            if (workflow.CurrentRenderState.Entries.Count > entryCount)
            {
                ClearComposer();
            }

            await send;
        }

        public async void OnCancelButtonClicked()
        {
            if (workflow == null)
            {
                return;
            }

            if (workflow.HasPendingConflict)
            {
                await workflow.DiscardPendingAsync();
            }
            else
            {
                workflow.CancelRequest();
            }
        }

        public void OnSettingsButtonClicked()
        {
            openSettings?.Invoke();
        }

        public void OnPreviewButtonClicked()
        {
            var preview = workflow?.ShowPreview();
            if (preview != null)
            {
                openPreview?.Invoke(preview);
            }
        }

        public async void OnUndoButtonClicked()
        {
            if (workflow != null)
            {
                await workflow.UndoLastAsync();
            }
        }

        public void Render(VizzyGptPanelRenderState? state)
        {
            if (state == null)
            {
                return;
            }

            messageList?.Render(state.Entries);

            if (panelRoot != null)
            {
                panelRoot.gameObject.SetActive(state.State != VizzyGptPanelState.Closed);
            }

            if (launcherButton != null)
            {
                launcherButton.gameObject.SetActive(state.State == VizzyGptPanelState.Closed);
            }

            if (sendButton != null)
            {
                sendButton.gameObject.SetActive(!state.CanCancel);
                sendButton.interactable = state.CanSend;
            }

            if (cancelButton != null)
            {
                cancelButton.gameObject.SetActive(state.CanCancel);
                cancelButton.interactable = state.CanCancel;
            }

            if (undoButton != null)
            {
                undoButton.interactable = state.CanUndo;
            }

            askToggle?.SetIsOnWithoutNotify(state.Mode == VizzyGptPanelMode.Ask);
            if (modifyToggle != null)
            {
                modifyToggle.gameObject.SetActive(state.CanModify);
                modifyToggle.SetIsOnWithoutNotify(state.Mode == VizzyGptPanelMode.Modify);
            }
        }

        private void OnPromptValueChanged(string value)
        {
            SetPromptText(value);
            if (CjkTextFontApplicator.ApplyToInput(composerInput, expectedCjkFont))
            {
                composerInput?.ForceLabelUpdate();
            }
        }

        private void ClearComposer()
        {
            prompt = string.Empty;
            composerInput?.SetTextWithoutNotify(string.Empty);
            composerInput?.ForceLabelUpdate();
        }

        private void LateUpdate()
        {
            RefreshDynamicTextFonts();
        }

        private void Update()
        {
            if (workflow == null || Time.unscaledTime < nextElapsedRefreshTime)
            {
                return;
            }

            nextElapsedRefreshTime = Time.unscaledTime + 0.25f;
            workflow.RefreshElapsed();
        }

        private void UnbindInput()
        {
            if (composerInput == null)
            {
                return;
            }

            composerInput.onValueChanged.RemoveListener(OnPromptValueChanged);
            composerInput = null;
        }

        private static T RequireElement<T>(IXmlLayout layout, string id) where T : Component
        {
            return layout.GetElementById<T>(id) ??
                throw new InvalidOperationException("Vizzy GPT XML is missing required " + typeof(T).Name + " '" + id + "'.");
        }

        private static void ConstrainPanelToParent(RectTransform panel)
        {
            if (!(panel.parent is RectTransform parent))
            {
                return;
            }

            var availableWidth = parent.rect.width - 32f;
            var availableHeight = parent.rect.height - 32f;
            if (availableWidth > 0f && panel.rect.width > availableWidth)
            {
                panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, availableWidth);
            }

            if (availableHeight > 0f && panel.rect.height > availableHeight)
            {
                panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, availableHeight);
            }
        }

        private static void ConstrainPanelChildWidth(RectTransform panel, RectTransform child)
        {
            var availableWidth = panel.rect.width - 32f;
            if (availableWidth > 0f && child.rect.width > availableWidth)
            {
                child.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, availableWidth);
            }
        }

        private void OnDestroy()
        {
            UnbindInput();
            workflow?.Dispose();
            workflow = null;
            openSettings = null;
            openPreview = null;
            expectedCjkFont = null;
            messageList?.Dispose();
            messageList = null;
        }
    }
}
