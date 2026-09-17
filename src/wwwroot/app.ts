import {
	ensureActivitySession,
	fetchJson,
	getIncomingCustomId,
	openActivityLink,
	openActivityShareMoment,
	shareActivityLink,
	setActivityPresence,
	type SessionResponse,
	type ViewerIdentity,
	getAuthConfig,
} from "./auth";

import {
	ArcElement,
	BarController,
	BarElement,
	CategoryScale,
	Chart,
	DoughnutController,
	Legend,
	LinearScale,
	Tooltip,
} from "chart.js";

Chart.register(
	ArcElement,
	BarController,
	BarElement,
	CategoryScale,
	DoughnutController,
	Legend,
	LinearScale,
	Tooltip,
);

type NotionListItem = { id: string; name: string; customId: string };
type StatusCount = { name: string; count: number };
type LanguageBreakdown = {
	language: string;
	statusCounts: Record<string, number>;
};
type Library = {
	name: string;
	language: string;
	status: string;
	version?: string | null;
	implementationUrl?: string | null;
};
type NotionDetails = {
	id: string;
	customId: string;
	title: string;
	description?: string | null;
	icon?: string | null;
	url?: string | null;
	lastEditedAt?: string | null;
	statusCounts: StatusCount[];
	languageBreakdown: LanguageBreakdown[];
	libraries: Library[];
};
type ShareImageResponse = { mediaUrl: string };

const statuses = [
	"Not Started",
	"In Progress",
	"In Review",
	"Ready For Release",
	"Released",
];
const colors: Record<string, string> = {
	"Not Started": "#80848e",
	"In Progress": "#da8a3a",
	"In Review": "#5865f2",
	"Ready For Release": "#f0b232",
	Released: "#23a55a",
};
const metricIds: Record<string, [string, string]> = {
	"Not Started": ["statusNotStarted", "statusNotStartedPercentage"],
	"In Progress": ["statusInProgress", "statusInProgressPercentage"],
	"In Review": ["statusInReview", "statusInReviewPercentage"],
	"Ready For Release": ["statusReady", "statusReadyPercentage"],
	Released: ["statusReleased", "statusReleasedPercentage"],
};

const state: {
	session: SessionResponse | null;
	notions: NotionListItem[];
	selectedId: string | null;
	currentNotion: NotionDetails | null;
	requestId: number;
	statusChart: any;
	languageChart: any;
} = {
	session: null,
	notions: [],
	selectedId: null,
	currentNotion: null,
	requestId: 0,
	statusChart: null,
	languageChart: null,
};

const quickLinkProvisioning = new Map<string, Promise<void>>();
const el = (id: string) => {
	const element = document.getElementById(id);
	if (!element) throw new Error(`Missing required Activity element: #${id}`);
	return element;
};
const authShell = el("authShell"),
	authMessage = el("authMessage"),
	authDetail = el("authDetail"),
	authProgress = el("authProgressBar");
const appShell = el("appShell"),
	notionList = el("notionList"),
	emptyState = el("emptyState"),
	dashboard = el("dashboard"),
	loading = el("loadingState"),
	errorBanner = el("errorBanner"),
	errorTitle = el("errorTitle"),
	errorMessage = el("errorMessage");
const refreshButton = el("refreshNotionsButton") as HTMLButtonElement,
	viewerName = el("viewerName"),
	viewerMeta = el("viewerMeta"),
	viewerAvatar = el("viewerAvatar") as HTMLImageElement,
	viewerFallback = el("viewerAvatarFallback"),
	viewerType = el("viewerType"),
	viewerLibrary = el("viewerLibrary");
const notionIcon = el("notionIcon"),
	notionTitle = el("notionTitle"),
	notionDescription = el("notionDescription"),
	syncState = el("syncState"),
	openNotion = el("openNotionButton") as HTMLButtonElement;
const shareActivityButton = el("shareActivityButton") as HTMLButtonElement,
	shareStatusChartButton = el("shareStatusChartButton") as HTMLButtonElement,
	shareLanguageChartButton = el(
		"shareLanguageChartButton",
	) as HTMLButtonElement;
const libraryCount = el("libraryCount"),
	libraryRows = el("libraryTableBody"),
	statusCanvas = el("statusChart") as HTMLCanvasElement,
	languageCanvas = el("languageChart") as HTMLCanvasElement;

void bootstrap();

async function bootstrap() {
	try {
		setAuth("Authenticating your Discord Activity session.", 12);
		const { config, session } = await ensureActivitySession(setAuth);
		state.session = session;
		populateViewer(session.user, session.localDev);
		document.body.classList.remove("auth-pending");
		authShell.classList.add("hidden");
		appShell.classList.remove("hidden");
		refreshButton.addEventListener("click", () => void loadNotions(true));
		if (!config.localDevActive && config.activityRequired) {
			await setActivityPresence({
				activity: {
					name: "Discord Library Development Tracking",
					applicationId: "1413632025025314991",
					state: "Loading tracked libraries and statistics",
					details: "Viewing tracked libraries",
					assets: {
						large_image: "cap",
						large_text: "CAP",
						small_image: "discord",
						small_text: "Discord",
					},
					emoji: { name: "CAPV2", id: "1342549467731333172" },
				},
			});
		}
		await loadNotions(false, await getIncomingCustomId(config));
	} catch (error) {
		setAuth(
			"Unable to authenticate this Discord Activity.",
			100,
			error instanceof Error ? error.message : String(error),
			true,
		);
	}
}

async function loadNotions(preserve = false, targetCustomId?: string | null) {
	refreshButton.disabled = true;
	renderListLoading();
	hideError();
	try {
		state.notions = await fetchJson<NotionListItem[]>(
			"/api/tracking/notions",
		);
		const linkedNotion = targetCustomId
			? state.notions.find((notion) => notion.customId === targetCustomId)
			: undefined;
		if (linkedNotion) state.selectedId = linkedNotion.id;
		else if (
			!preserve ||
			!state.notions.some((notion) => notion.id === state.selectedId)
		)
			state.selectedId = state.notions[0]?.id ?? null;
		renderList();
		if (state.selectedId) await selectNotion(state.selectedId, preserve);
		else showEmpty("No tracked notions are configured yet.");
	} catch (error) {
		state.notions = [];
		state.selectedId = null;
		state.currentNotion = null;
		renderList();
		showEmpty("Tracked notions could not be loaded.");
		showError(error, "Couldn't load the tracked notions");
	} finally {
		refreshButton.disabled = false;
	}
}

async function selectNotion(id: string, refresh = false) {
	if (!state.notions.some((notion) => notion.id === id)) return;
	state.selectedId = id;
	state.currentNotion = null;
	renderList();
	const requestId = ++state.requestId;
	loading.classList.remove("hidden");
	hideError();
	try {
		const notion = await fetchJson<NotionDetails>(
			`/api/tracking/notions/${encodeURIComponent(id)}${refresh ? "?refresh=true" : ""}`,
		);
		if (requestId === state.requestId) await renderDetails(notion);
	} catch (error) {
		if (requestId === state.requestId) {
			showEmpty("This notion could not be loaded.");
			showError(error, "Couldn't load this notion");
		}
	} finally {
		if (requestId === state.requestId) loading.classList.add("hidden");
	}
}

function renderListLoading() {
	notionList.replaceChildren();
	for (let index = 0; index < 3; index++) {
		const item = document.createElement("div");
		item.className = "notion-skeleton";
		notionList.append(item);
	}
}

function renderList() {
	notionList.replaceChildren();
	if (!state.notions.length) {
		const text = document.createElement("p");
		text.textContent = "No tracked notions";
		notionList.append(text);
		return;
	}
	for (const notion of state.notions) {
		const button = document.createElement("button");
		button.type = "button";
		button.className = "notion-item";
		button.classList.toggle("active", notion.id === state.selectedId);
		button.setAttribute(
			"aria-current",
			notion.id === state.selectedId ? "page" : "false",
		);
		button.addEventListener("click", () => void selectNotion(notion.id));
		const icon = document.createElement("span");
		icon.className = "notion-item-icon";
		icon.textContent = "📄";
		const title = document.createElement("span");
		title.className = "notion-item-title";
		title.textContent = notion.name;
		button.append(icon, title);
		notionList.append(button);
	}
}

async function renderDetails(notion: NotionDetails) {
	state.currentNotion = notion;
	emptyState.classList.add("hidden");
	dashboard.classList.remove("hidden");
	notionIcon.textContent = notion.icon || "📄";
	notionTitle.textContent = notion.title;
	notionDescription.textContent =
		notion.description || "No description is available for this notion.";
	const syncText = syncState.querySelector("span:last-child");
	if (syncText)
		syncText.textContent = notion.lastEditedAt
			? `Updated ${new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(new Date(notion.lastEditedAt))}`
			: "Loaded from Notion";
	openNotion.disabled = !notion.url;
	openNotion.onclick = notion.url
		? () => void openActivityLink(notion.url!)
		: null;
	shareActivityButton.disabled = !notion.customId;
	shareActivityButton.onclick = () => void shareCurrentActivity(notion);
	renderMetrics(notion.statusCounts);
	renderCharts(notion);
	renderLibraries(notion.libraries);
	shareStatusChartButton.disabled = !state.statusChart;
	shareLanguageChartButton.disabled = !state.languageChart;
	shareStatusChartButton.onclick = () =>
		void shareChart(
			notion,
			state.statusChart,
			"Implementation status",
			shareStatusChartButton,
		);
	shareLanguageChartButton.onclick = () =>
		void shareChart(
			notion,
			state.languageChart,
			"Language support",
			shareLanguageChartButton,
		);
	void provisionQuickLink(notion.id);

	const config = await getAuthConfig();
	if (!config.localDevActive && config.activityRequired) {
		await setActivityPresence({
			activity: {
				name: "Discord Library Development Tracking",
				applicationId: "1413632025025314991",
				state: "Viewing library statistics for " + notion.title,
				details: "Viewing tracked libraries",
				assets: {
					large_image: "cap",
					large_text: "CAP",
					small_image: "discord",
					small_text: "Discord",
				},
				emoji: { name: "CAPV2", id: "1342549467731333172" },
			},
		});
	}
}

function renderMetrics(counts: StatusCount[]) {
	const values = new Map(counts.map((item) => [item.name, item.count]));
	const total = counts.reduce((sum, item) => sum + item.count, 0);
	for (const status of statuses) {
		const [countId, percentageId] = metricIds[status];
		const value = values.get(status) ?? 0;
		el(countId).textContent = String(value);
		el(percentageId).textContent = total
			? `${Math.round((value / total) * 100)}%`
			: "0%";
	}
}

function renderCharts(notion: NotionDetails) {
	state.statusChart?.destroy();
	state.languageChart?.destroy();
	const labels = notion.statusCounts.map((item) => item.name);
	const palette = labels.map((label) => colors[label] ?? "#747f8d");
	state.statusChart = new Chart(statusCanvas, {
		type: "doughnut",
		data: {
			labels,
			datasets: [
				{
					data: notion.statusCounts.map((item) => item.count),
					backgroundColor: palette,
					borderWidth: 0,
				},
			],
		},
		options: {
			responsive: true,
			maintainAspectRatio: false,
			plugins: { legend: { position: "bottom" } },
		},
	});
	state.languageChart = new Chart(languageCanvas, {
		type: "bar",
		data: {
			labels: notion.languageBreakdown.map((item) => item.language),
			datasets: labels.map((status, index) => ({
				label: status,
				data: notion.languageBreakdown.map(
					(item) => item.statusCounts[status] ?? 0,
				),
				backgroundColor: palette[index],
				borderWidth: 0,
			})),
		},
		options: {
			responsive: true,
			maintainAspectRatio: false,
			scales: {
				x: { stacked: true },
				y: {
					stacked: true,
					beginAtZero: true,
					ticks: { precision: 0 },
				},
			},
			plugins: { legend: { position: "bottom" } },
		},
	});
}

function renderLibraries(libraries: Library[]) {
	libraryCount.textContent = `${libraries.length} ${libraries.length === 1 ? "library" : "libraries"}`;
	libraryRows.replaceChildren();
	for (const library of libraries) {
		const row = document.createElement("tr");
		row.append(
			cell(library.name),
			cell(library.language),
			statusCell(library.status),
			cell(library.version || "—"),
			implementationCell(library.implementationUrl),
		);
		libraryRows.append(row);
	}
}

async function provisionQuickLink(notionId: string) {
	const existingAttempt = quickLinkProvisioning.get(notionId);
	if (existingAttempt) return existingAttempt;

	const attempt = (async () => {
		try {
			await fetchJson<void>(
				`/api/tracking/notions/${encodeURIComponent(notionId)}/quick-link`,
				{ method: "POST" },
			);
		} catch {
			// Preview provisioning is intentionally best-effort; custom-id routing still works.
		}
	})();
	quickLinkProvisioning.set(notionId, attempt);
	return attempt;
}

async function shareCurrentActivity(notion: NotionDetails) {
	shareActivityButton.disabled = true;
	try {
		await provisionQuickLink(notion.id);
		await shareActivityLink(
			notion.customId,
			`Open ${notion.title} in Library Tracking.`,
		);
	} catch (error) {
		showError(error, "Couldn't share this Activity link");
	} finally {
		shareActivityButton.disabled = false;
	}
}

async function shareChart(
	notion: NotionDetails,
	chart: any,
	chartTitle: string,
	button: HTMLButtonElement,
) {
	button.disabled = true;
	try {
		const image = await createChartShareImage(notion, chart, chartTitle);
		const form = new FormData();
		form.append("image", image);
		const response = await fetchJson<ShareImageResponse>(
			`/api/tracking/notions/${encodeURIComponent(notion.id)}/share-image`,
			{ method: "POST", body: form },
		);
		await openActivityShareMoment(response.mediaUrl);
	} catch (error) {
		showError(error, "Couldn't share this chart image");
	} finally {
		button.disabled = false;
	}
}

async function createChartShareImage(
	notion: NotionDetails,
	chart: any,
	chartTitle: string,
) {
	const canvas = document.createElement("canvas");
	canvas.width = 1600;
	canvas.height = 900;
	const context = canvas.getContext("2d");
	if (!context || !chart?.canvas)
		throw new Error("The chart is not ready to share.");
	context.fillStyle = "#313338";
	context.fillRect(0, 0, canvas.width, canvas.height);
	context.fillStyle = "#2b2d31";
	context.fillRect(48, 48, canvas.width - 96, canvas.height - 96);
	context.strokeStyle = "rgba(255, 255, 255, 0.12)";
	context.lineWidth = 2;
	context.strokeRect(48, 48, canvas.width - 96, canvas.height - 96);
	context.font = '600 28px "gg sans", "Noto Sans", sans-serif';
	context.fillStyle = "#949ba4";
	context.fillText("LIBRARY DEVELOPMENT TRACKING", 100, 120);
	context.font = '700 48px "gg sans", "Noto Sans", sans-serif';
	context.fillStyle = "#f2f3f5";
	context.fillText(
		`${notion.icon || "📄"} ${truncateText(notion.title, 48)}`,
		100,
		190,
	);
	context.font = '400 28px "gg sans", "Noto Sans", sans-serif';
	context.fillStyle = "#b5bac1";
	const descriptionBottom = drawWrappedText(
		context,
		notion.description || "Read-only implementation statistics.",
		100,
		240,
		1350,
		38,
		2,
	);
	context.font = '600 30px "gg sans", "Noto Sans", sans-serif';
	context.fillStyle = "#f2f3f5";
	context.fillText(chartTitle, 100, Math.max(descriptionBottom + 62, 350));
	const chartTop = Math.max(descriptionBottom + 92, 390);
	const chartHeight = canvas.height - chartTop - 90;
	const chartLeft = 90;
	const chartWidth = canvas.width - chartLeft * 2;
	context.fillStyle = "#232428";
	context.fillRect(chartLeft, chartTop - 25, chartWidth, chartHeight + 35);
	drawImageContained(
		context,
		chart.canvas,
		chartLeft + 16,
		chartTop - 10,
		chartWidth - 32,
		chartHeight,
	);
	const blob = await new Promise<Blob>((resolve, reject) =>
		canvas.toBlob(
			(value) =>
				value
					? resolve(value)
					: reject(
							new Error("The chart image could not be created."),
						),
			"image/png",
		),
	);
	return new File(
		[blob],
		`${notion.id}-${chartTitle.toLowerCase().replaceAll(" ", "-")}.png`,
		{ type: "image/png" },
	);
}

function drawImageContained(
	context: CanvasRenderingContext2D,
	image: CanvasImageSource & { width: number; height: number },
	x: number,
	y: number,
	maximumWidth: number,
	maximumHeight: number,
) {
	const scale = Math.min(
		maximumWidth / image.width,
		maximumHeight / image.height,
	);
	const width = Math.round(image.width * scale);
	const height = Math.round(image.height * scale);
	context.drawImage(
		image,
		x + Math.round((maximumWidth - width) / 2),
		y + Math.round((maximumHeight - height) / 2),
		width,
		height,
	);
}

function drawWrappedText(
	context: CanvasRenderingContext2D,
	text: string,
	x: number,
	y: number,
	maximumWidth: number,
	lineHeight: number,
	maximumLines: number,
) {
	const words = text.trim().split(/\s+/);
	let line = "";
	let lineCount = 0;
	for (const word of words) {
		const nextLine = line ? `${line} ${word}` : word;
		if (context.measureText(nextLine).width <= maximumWidth || !line) {
			line = nextLine;
			continue;
		}
		context.fillText(line, x, y + lineCount * lineHeight);
		lineCount++;
		if (lineCount === maximumLines) return y + lineCount * lineHeight;
		line = word;
	}
	if (line && lineCount < maximumLines) {
		context.fillText(line, x, y + lineCount * lineHeight);
		lineCount++;
	}
	return y + lineCount * lineHeight;
}

function cell(value: string) {
	const result = document.createElement("td");
	result.textContent = value;
	return result;
}
function statusCell(status: string) {
	const result = document.createElement("td"),
		tag = document.createElement("span");
	tag.className = "status-tag";
	tag.dataset.status =
		(
			{
				"Not Started": "not-started",
				"In Progress": "in-progress",
				"In Review": "in-review",
				"Ready For Release": "ready",
				Released: "released",
			} as Record<string, string>
		)[status] ?? "unknown";
	tag.textContent = status;
	result.append(tag);
	return result;
}
function implementationCell(url?: string | null) {
	const result = document.createElement("td");
	if (!url) {
		result.textContent = "—";
		return result;
	}
	const button = document.createElement("button");
	button.type = "button";
	button.className = "button button-secondary";
	button.textContent = "Open";
	button.addEventListener("click", () => void openActivityLink(url));
	result.append(button);
	return result;
}
function showEmpty(message: string) {
	dashboard.classList.add("hidden");
	emptyState.classList.remove("hidden");
	emptyState.querySelector("p")!.textContent = message;
}
function showError(error: unknown, title = "Something went wrong") {
	errorTitle.textContent = title;
	const message = error instanceof Error ? error.message : String(error);
	errorMessage.textContent = /^5\d\d(?:\s|$)/.test(message)
		? "The server could not complete that request. Check the Activity host log for the underlying error."
		: message;
	errorBanner.classList.remove("hidden");
}
function hideError() {
	errorTitle.textContent = "Something went wrong";
	errorMessage.textContent = "";
	errorBanner.classList.add("hidden");
}
function setAuth(
	message: string,
	progress: number,
	detail?: string,
	failed = false,
) {
	authMessage.textContent = message;
	authProgress.style.width = `${Math.max(0, Math.min(progress, 100))}%`;
	authShell.classList.toggle("auth-shell--error", failed);
	authDetail.textContent = detail || "";
	authDetail.classList.toggle("hidden", !detail);
}
function populateViewer(viewer: ViewerIdentity, localDev: boolean) {
	const name = viewer.displayName?.trim() || viewer.username;
	viewerName.textContent = name;
	viewerMeta.textContent = localDev
		? `Local development • @${viewer.username}`
		: `@${viewer.username}`;
	viewerFallback.textContent = Array.from(name)[0]?.toUpperCase() || "?";
	viewerType.textContent = localDev ? "Admin" : viewer.type;
	if (localDev || viewer.type === "Admin" || viewer.type === "Employee") {
		viewerLibrary.innerHTML = "<i>All</i>";
	} else if (viewer.libraries !== undefined) {
		let libraries: string = "";
		let first = true;
		viewer.libraries.forEach((library) => {
			if (library !== undefined && library !== "undefined") {
				if (!first) {
					libraries = libraries + ", "
				} else {
					first = false;
				}
				libraries = libraries + `${library}`;
			}
		});
		viewerLibrary.innerHTML = libraries;
	} else {
		viewerLibrary.innerHTML = "<i>None</i>"
	}
	if (!viewer.avatarHash) {
		viewerAvatar.classList.add("hidden");
		viewerFallback.classList.remove("hidden");
		return;
	}
	const extension = viewer.avatarHash.startsWith("a_") ? "gif" : "webp";
	viewerAvatar.src = `https://cdn.discordapp.com/avatars/${encodeURIComponent(viewer.id)}/${viewer.avatarHash}.${extension}?size=128`;
	viewerAvatar.alt = `${name}'s avatar`;
	viewerAvatar.classList.remove("hidden");
	viewerFallback.classList.add("hidden");
	viewerAvatar.addEventListener(
		"error",
		() => {
			viewerAvatar.classList.add("hidden");
			viewerFallback.classList.remove("hidden");
		},
		{ once: true },
	);
}
function truncateText(value: string, maximumLength: number) {
	return value.length <= maximumLength
		? value
		: `${value.slice(0, Math.max(1, maximumLength - 1)).trimEnd()}…`;
}
