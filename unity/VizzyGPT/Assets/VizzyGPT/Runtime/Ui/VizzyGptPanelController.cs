#nullable enable

using System;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using ModApi.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VizzyGPT.Core.Api;
using VizzyGPT.Core.Changes;
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
            bool canUndo)
        {
            Mode = mode;
            State = state;
            StatusText = statusText;
            TranscriptText = transcriptText;
            CanSend = canSend;
            CanCancel = canCancel;
            CanPreview = canPreview;
            CanUndo = canUndo;
        }

        public VizzyGptPanelMode Mode { get; }
        public VizzyGptPanelState State { get; }
        public string StatusText { get; }
        public string TranscriptText { get; }
        public bool CanSend { get; }
        public bool CanCancel { get; }
        public bool CanPreview { get; }
        public bool CanUndo { get; }
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

        private CancellationTokenSource? requestCancellation;
        private ChangeSession? session;
        private AppliedChange? undoSession;
        private long requestGeneration;
        private bool disposed;
        private bool pendingConflict;
        private string? restoredPendingFingerprint;

        public VizzyGptPanelWorkflow(
            IVizzyRuntimeAdapter adapter,
            Func<AiRequest, CancellationToken, Task<AiResponse>> sendAsync,
            Func<string, string, AiRequest> createRequest,
            Func<string, string, DateTime, CancellationToken, Task> saveBackupAsync,
            Func<VizzyNodeCatalog> createCatalog,
            Func<DateTime> utcNow,
            Action<VizzyGptPanelRenderState> render,
            VizzyGptWorkflowEnvironment? environment = null)
        {
            this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            this.sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
            this.createRequest = createRequest ?? throw new ArgumentNullException(nameof(createRequest));
            this.saveBackupAsync = saveBackupAsync ?? throw new ArgumentNullException(nameof(saveBackupAsync));
            this.createCatalog = createCatalog ?? throw new ArgumentNullException(nameof(createCatalog));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.render = render ?? throw new ArgumentNullException(nameof(render));
            this.environment = environment ?? VizzyGptWorkflowEnvironment.CreateDefault();
            RenderState();
        }

        public VizzyGptPanelMode Mode { get; private set; } = VizzyGptPanelMode.Ask;

        public VizzyGptPanelState State { get; private set; } = VizzyGptPanelState.Closed;

        public string StatusText { get; private set; } = string.Empty;

        public string TranscriptText { get; private set; } = string.Empty;

        public bool CanApply => State == VizzyGptPanelState.PreviewReady && session != null && IsSessionCurrent();

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
                    session != null ? "Pending flight preview ready." : "Ready.");
            }
        }

        public void ClosePanel()
        {
            if (disposed)
            {
                return;
            }

            CancelRequest();
            requestGeneration++;
            if (restoredPendingFingerprint == null)
            {
                session = null;
            }
            Transition(VizzyGptPanelState.Closed, string.Empty);
        }

        public void SetMode(VizzyGptPanelMode mode)
        {
            ThrowIfDisposed();
            if (!Enum.IsDefined(typeof(VizzyGptPanelMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
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
            var generation = ++requestGeneration;
            session = null;
            pendingConflict = false;
            var cancellation = new CancellationTokenSource();
            requestCancellation = cancellation;
            var cancellationToken = cancellation.Token;
            Transition(VizzyGptPanelState.Sending, "Sending request.");

            try
            {
                var requestContext = BuildRequestContext();
                var response = await sendAsync(createRequest(prompt, requestContext.AiContext), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentRequest(generation, cancellation))
                {
                    return;
                }
                AppendTranscript(response.Message);

                if (requestContext.Mode == VizzyGptPanelMode.Ask)
                {
                    session = null;
                    Transition(VizzyGptPanelState.Idle, "Response received.");
                    return;
                }

                if (!TryCreateSession(response, requestContext, out var createdSession, out var error))
                {
                    session = null;
                    Transition(VizzyGptPanelState.Error, error);
                    return;
                }

                session = createdSession;
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
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentRequest(generation, cancellation))
                {
                    Transition(VizzyGptPanelState.Idle, "Request cancelled.");
                }
            }
            catch (Exception exception)
            {
                if (IsCurrentRequest(generation, cancellation))
                {
                    session = null;
                    Transition(VizzyGptPanelState.Error, exception.Message);
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

        public async Task RestorePendingAsync()
        {
            ThrowIfDisposed();
            if (environment.IsFlight)
            {
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
            session = null;
            if (restoredPendingFingerprint != null)
            {
                await environment.DeletePendingAsync(restoredPendingFingerprint, CancellationToken.None);
                restoredPendingFingerprint = null;
            }
            Transition(VizzyGptPanelState.Idle, "Changes applied.");
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
                return new RequestContext(
                    environment.IsFlight
                        ? environment.BuildFlightContext() + "\nAsk mode does not permit program mutation."
                        : "EDITOR CONTEXT\nAsk mode does not permit program mutation.",
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

        private bool TryCreateSession(
            AiResponse response,
            RequestContext requestContext,
            out ChangeSession? createdSession,
            out string error)
        {
            createdSession = null;
            if (!response.CanApply || response.Patch == null)
            {
                error = "The response did not contain an applicable change.";
                return false;
            }

            if (requestContext.SourceDocument == null || requestContext.SourceHash == null)
            {
                error = "Modify mode did not capture an editor program.";
                return false;
            }

            if (!TryReadCurrent(out _, out var currentHash, out var readError))
            {
                error = readError ?? "Unable to read the current Vizzy program.";
                return false;
            }

            if (!string.Equals(currentHash, requestContext.SourceHash, StringComparison.Ordinal))
            {
                error = "The Vizzy program changed while the request was in progress.";
                return false;
            }

            try
            {
                var result = VizzyPatchEngine.Apply(requestContext.SourceDocument, response.Patch);
                var report = new VizzyProgramValidator(adapter.ValidateWithProgramSerializer)
                    .Validate(result.Document, createCatalog());
                if (!report.IsValid)
                {
                    error = report.Errors[0].Message;
                    return false;
                }

                createdSession = ChangeSession.Create(requestContext.SourceDocument, response.Patch, result, report);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
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
                State != VizzyGptPanelState.Closed && !isBusy,
                State == VizzyGptPanelState.Sending || pendingConflict,
                CanApply,
                CanUndo);
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
        private TMP_InputField? promptInput;
        private TMP_Text? transcriptText;
        private TMP_Text? statusText;
        private RectTransform? panelRoot;
        private Button? launcherButton;
        private Button? sendButton;
        private Button? cancelButton;
        private Button? previewButton;
        private Button? undoButton;
        private Image? pendingIndicator;
        private Toggle? askToggle;
        private Toggle? modifyToggle;

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
        }

        public void Bind(IXmlLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            UnbindInput();
            promptInput = RequireElement<TMP_InputField>(layout, "prompt-input");
            transcriptText = RequireElement<TMP_Text>(layout, "transcript-text");
            statusText = RequireElement<TMP_Text>(layout, "status-text");
            panelRoot = RequireElement<RectTransform>(layout, "vizzy-gpt-panel");
            launcherButton = RequireElement<Button>(layout, "gpt-launcher-button");
            sendButton = RequireElement<Button>(layout, "send-button");
            cancelButton = RequireElement<Button>(layout, "cancel-button");
            previewButton = RequireElement<Button>(layout, "preview-button");
            undoButton = RequireElement<Button>(layout, "undo-button");
            pendingIndicator = RequireElement<Image>(layout, "pending-indicator");
            askToggle = RequireElement<Toggle>(layout, "ask-toggle");
            modifyToggle = RequireElement<Toggle>(layout, "modify-toggle");

            if (promptInput != null)
            {
                // The stock TMP input owns focus; ModApi exposes its UI focus gates as read-only.
                promptInput.onValueChanged.AddListener(OnPromptValueChanged);
                promptInput.text = prompt;
            }

            Render(workflow?.CurrentRenderState);
        }

        public void SetPromptText(string value)
        {
            prompt = value ?? string.Empty;
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
            if (workflow != null)
            {
                await workflow.SendPromptAsync(prompt);
            }
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

            if (transcriptText != null)
            {
                transcriptText.text = state.TranscriptText;
            }

            if (statusText != null)
            {
                statusText.text = state.StatusText;
            }

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
                sendButton.interactable = state.CanSend;
            }

            if (cancelButton != null)
            {
                cancelButton.interactable = state.CanCancel;
            }

            if (previewButton != null)
            {
                previewButton.interactable = state.CanPreview;
            }

            if (undoButton != null)
            {
                undoButton.interactable = state.CanUndo;
            }

            if (pendingIndicator != null)
            {
                pendingIndicator.gameObject.SetActive(state.State == VizzyGptPanelState.PreviewReady);
            }

            askToggle?.SetIsOnWithoutNotify(state.Mode == VizzyGptPanelMode.Ask);
            modifyToggle?.SetIsOnWithoutNotify(state.Mode == VizzyGptPanelMode.Modify);
        }

        private void OnPromptValueChanged(string value)
        {
            SetPromptText(value);
        }

        private void UnbindInput()
        {
            if (promptInput == null)
            {
                return;
            }

            promptInput.onValueChanged.RemoveListener(OnPromptValueChanged);
            promptInput = null;
        }

        private static T RequireElement<T>(IXmlLayout layout, string id) where T : Component
        {
            return layout.GetElementById<T>(id) ??
                throw new InvalidOperationException("Vizzy GPT XML is missing required " + typeof(T).Name + " '" + id + "'.");
        }

        private void OnDestroy()
        {
            UnbindInput();
            workflow?.Dispose();
            workflow = null;
            openSettings = null;
            openPreview = null;
        }
    }
}
