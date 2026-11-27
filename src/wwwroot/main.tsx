import { useEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import {
	Badge,
	Button,
	Checkbox,
	Heading,
	Modal,
	ModalActionBar,
	ModalBody,
	ModalHeader,
	ProgressBar,
	Popover,
	Select,
	Table,
	TableBody,
	TableCell,
	TableColumn,
	TableHeader,
	TableRow,
	TextArea,
	TextInput,
	Tooltip as ManaTooltip,
} from "@discord/mana";
import {
	ArcElement,
	BarController,
	BarElement,
	CategoryScale,
	Chart,
	DoughnutController,
	Legend,
	LinearScale,
	Tooltip as ChartTooltip,
} from "chart.js";
import {
	ensureActivitySession,
	fetchJson,
	getActivityDeepLinks,
	getIncomingCustomId,
	getAuthConfig,
	openActivityLink,
	openActivityShareContent,
	openActivityShareMoment,
	shareActivityLink,
	setActivityPresence,
	type SessionResponse,
	type ViewerIdentity,
} from "./auth";

Chart.register(
	ArcElement,
	BarController,
	BarElement,
	CategoryScale,
	DoughnutController,
	Legend,
	LinearScale,
	ChartTooltip,
);

type NotionListItem = { id: string; name: string; customId: string };
type AdminNotion = {
	id: string;
	name: string;
	databaseId?: string | null;
	dataSourceId: string;
	enabled: boolean;
	isTemplate: boolean;
};
type NotionMetadata = { title: string; description: string };
type NotionCreationResponse = {
	id: string;
	name: string;
	enabled: boolean;
	titleUpdated: boolean;
	descriptionUpdated: boolean;
};
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
	notes?: string | null;
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
type ChartKind = "doughnut" | "bar";
type ActivityRoute = {
	pageId: string;
	target: string | null;
};
type IncomingActivityRoute = ActivityRoute & { sequence: number };

function createActivityRoute(pageId: string, target: string) {
	const compactPageId = pageId.replaceAll("-", "").toLowerCase();
	return `/${compactPageId}?${new URLSearchParams({ target: target.toLowerCase() })}`;
}

function parseActivityRoute(value: string): ActivityRoute | null {
	try {
		const route = value.startsWith("#") ? value.slice(1) : value;
		const url = new URL(
			route.startsWith("/") ? route : `/${route}`,
			"https://activity.invalid",
		);
		const pageId = url.pathname
			.split("/")
			.filter(Boolean)[0]
			?.replaceAll("-", "")
			.toLowerCase();
		if (!pageId || !/^[0-9a-f]{32}$/.test(pageId)) return null;
		return {
			pageId,
			target: url.searchParams.get("target")?.toLowerCase() ?? null,
		};
	} catch {
		return null;
	}
}

const statuses = [
	"Not Started",
	"In Progress",
	"In Review",
	"Ready For Release",
	"Released",
];
const statusColors: Record<string, string> = {
	"Not Started": "#80848e",
	"In Progress": "#da8a3a",
	"In Review": "#5865f2",
	"Ready For Release": "#f0b232",
	Released: "#23a55a",
};
const quickLinkProvisioning = new Map<string, Promise<void>>();

function StatusChart({
	kind,
	notion,
	onReady,
}: {
	kind: ChartKind;
	notion: NotionDetails;
	onReady: (chart: Chart | null) => void;
}) {
	const canvasRef = useRef<HTMLCanvasElement>(null);
	useEffect(() => {
		const canvas = canvasRef.current;
		if (!canvas) return;
		const labels = notion.statusCounts.map((item) => item.name);
		const palette = labels.map((label) => statusColors[label] ?? "#747f8d");
		const chart =
			kind === "doughnut"
				? new Chart(canvas, {
						type: "doughnut",
						data: {
							labels,
							datasets: [
								{
									data: notion.statusCounts.map(
										(item) => item.count,
									),
									backgroundColor: palette,
									borderWidth: 0,
								},
							],
						},
						options: {
							responsive: true,
							maintainAspectRatio: false,
							plugins: {
								legend: {
									position: "bottom",
									labels: { color: "#b5bac1", padding: 18 },
								},
							},
						},
					})
				: new Chart(canvas, {
						type: "bar",
						data: {
							labels: notion.languageBreakdown.map(
								(item) => item.language,
							),
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
								x: {
									stacked: true,
									ticks: { color: "#b5bac1" },
									grid: { color: "rgba(255,255,255,.06)" },
								},
								y: {
									stacked: true,
									beginAtZero: true,
									ticks: { precision: 0, color: "#b5bac1" },
									grid: { color: "rgba(255,255,255,.06)" },
								},
							},
							plugins: {
								legend: {
									position: "bottom",
									labels: { color: "#b5bac1", padding: 18 },
								},
							},
						},
					});
		onReady(chart);
		return () => {
			onReady(null);
			chart.destroy();
		};
	}, [kind, notion]);
	return (
		<canvas
			ref={canvasRef}
			aria-label={
				kind === "doughnut"
					? "Implementation status chart"
					: "Language support chart"
			}
			role="img"
		/>
	);
}

function App() {
	const [authMessage, setAuthMessage] = useState("Connecting to Discord…");
	const [authProgress, setAuthProgress] = useState(8);
	const [authDetail, setAuthDetail] = useState("");
	const [authFailed, setAuthFailed] = useState(false);
	const [session, setSession] = useState<SessionResponse | null>(null);
	const [notions, setNotions] = useState<NotionListItem[]>([]);
	const [selectedId, setSelectedId] = useState<string | null>(null);
	const [currentNotion, setCurrentNotion] = useState<NotionDetails | null>(
		null,
	);
	const [loadingNotions, setLoadingNotions] = useState(false);
	const [loadingDetails, setLoadingDetails] = useState(false);
	const [directLinksAvailable, setDirectLinksAvailable] = useState(false);
	const [incomingRoute, setIncomingRoute] =
		useState<IncomingActivityRoute | null>(null);
	const [targetLibrary, setTargetLibrary] = useState<{
		pageId: string;
		name: string;
	} | null>(null);
	const [refreshing, setRefreshing] = useState(false);
	const [error, setError] = useState<{
		title: string;
		message: string;
	} | null>(null);
	const [adminNotions, setAdminNotions] = useState<AdminNotion[]>([]);
	const [adminStatus, setAdminStatus] = useState("");
	const [adminLoading, setAdminLoading] = useState(false);
	const [manageOpen, setManageOpen] = useState(false);
	const [editLibrary, setEditLibrary] = useState<Library | null>(null);
	const [editLibraryBusy, setEditLibraryBusy] = useState(false);
	const [editStatus, setEditStatus] = useState("");
	const [editUrl, setEditUrl] = useState("");
	const [editVersion, setEditVersion] = useState("");
	const [editNotes, setEditNotes] = useState("");
	const [createTitle, setCreateTitle] = useState("");
	const [createDescription, setCreateDescription] = useState("");
	const [createAutoEnable, setCreateAutoEnable] = useState(true);
	const [createBusy, setCreateBusy] = useState(false);
	const [metadataNotion, setMetadataNotion] = useState<AdminNotion | null>(
		null,
	);
	const [metadataTitle, setMetadataTitle] = useState("");
	const [metadataDescription, setMetadataDescription] = useState("");
	const [metadataBusy, setMetadataBusy] = useState(false);
	const [shareBusy, setShareBusy] = useState(false);
	const [shareChartBusy, setShareChartBusy] = useState<string | null>(null);
	const [shareChartDialogKind, setShareChartDialogKind] = useState<
		"status" | "language" | null
	>(null);
	const [shareChartContent, setShareChartContent] = useState("");
	const statusChart = useRef<Chart | null>(null);
	const languageChart = useRef<Chart | null>(null);
	const requestId = useRef(0);
	const deepLinksRef = useRef<
		Awaited<ReturnType<typeof getActivityDeepLinks>>
	>(null);
	const routeSequenceRef = useRef(0);
	const appliedRouteSequenceRef = useRef(0);
	const mainContentRef = useRef<HTMLElement>(null);
	const canManageNotions =
		!!session &&
		(session.localDev ||
			session.user.type === "Admin" ||
			session.user.type === "Employee");

	const showError = (cause: unknown, title = "Something went wrong") => {
		const message = cause instanceof Error ? cause.message : String(cause);
		setError({
			title,
			message: /^5\d\d(?:\s|$)/.test(message)
				? "The server could not complete that request. Check the Activity host log for the underlying error."
				: message,
		});
	};
	const clearError = () => setError(null);
	const setAuth = (
		message: string,
		progress: number,
		detail?: string,
		failed = false,
	) => {
		setAuthMessage(message);
		setAuthProgress(Math.max(0, Math.min(progress, 100)));
		setAuthDetail(detail ?? "");
		setAuthFailed(failed);
	};

	async function selectNotion(
		id: string,
		refresh = false,
		availableNotions = notions,
	) {
		if (!availableNotions.some((notion) => notion.id === id)) return;
		setSelectedId(id);
		setCurrentNotion(null);
		setLoadingDetails(true);
		clearError();
		const thisRequest = ++requestId.current;
		try {
			const notion = await fetchJson<NotionDetails>(
				`/api/tracking/notions/${encodeURIComponent(id)}${refresh ? "?refresh=true" : ""}`,
			);
			if (thisRequest === requestId.current) {
				setCurrentNotion(notion);
				void provisionQuickLink(notion.id);
				const config = await getAuthConfig();
				if (!config.localDevActive && config.activityRequired)
					await setActivityPresence({
						activity: {
							type: 0,
							name: "Discord Library Development Tracking",
							applicationId: "1413632025025314991",
							state: `Viewing library statistics for ${notion.title}`,
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
		} catch (cause) {
			if (thisRequest === requestId.current)
				showError(cause, "Couldn't load this notion");
		} finally {
			if (thisRequest === requestId.current) setLoadingDetails(false);
		}
	}

	async function loadNotions(
		preserve = false,
		targetCustomId?: string | null,
	) {
		setRefreshing(true);
		setLoadingNotions(true);
		clearError();
		try {
			const loaded = await fetchJson<NotionListItem[]>(
				"/api/tracking/notions",
			);
			setNotions(loaded);
			const launchRoute = targetCustomId
				? parseActivityRoute(targetCustomId)
				: null;
			const routedNotion = launchRoute
				? loaded.find(
						(notion) =>
							notion.id.replaceAll("-", "").toLowerCase() ===
							launchRoute.pageId,
						)
				: undefined;
			const linked = targetCustomId
				? loaded.find((notion) => notion.customId === targetCustomId)
				: undefined;
			const nextId =
				routedNotion?.id ??
				linked?.id ??
				(preserve && loaded.some((notion) => notion.id === selectedId)
					? selectedId
					: (loaded[0]?.id ?? null));
			setTargetLibrary(
				routedNotion && launchRoute?.target
					? {
							pageId: routedNotion.id
								.replaceAll("-", "")
								.toLowerCase(),
							name: launchRoute.target,
						}
					: null,
			);
			setSelectedId(nextId);
			if (nextId) await selectNotion(nextId, preserve, loaded);
			else setCurrentNotion(null);
		} catch (cause) {
			setNotions([]);
			setSelectedId(null);
			setCurrentNotion(null);
			showError(cause, "Couldn't load the tracked notions");
		} finally {
			setRefreshing(false);
			setLoadingNotions(false);
		}
	}

	async function loadAdminNotions() {
		if (!canManageNotions) return;
		setAdminLoading(true);
		setAdminStatus("Loading enabled and disabled trackers…");
		try {
			setAdminNotions(
				await fetchJson<AdminNotion[]>("/api/admin/notions"),
			);
			setAdminStatus("");
		} catch (cause) {
			setAdminStatus("Tracker management could not be loaded.");
			showError(cause, "Couldn't load Notion management");
		} finally {
			setAdminLoading(false);
		}
	}

	useEffect(() => {
		let alive = true;
		let unsubscribeDeepLinks: (() => void) | null = null;
		void (async () => {
			try {
				setAuth("Authenticating your Discord Activity session.", 12);
				const { config, session: newSession } =
					await ensureActivitySession(
						(message, progress) =>
							alive && setAuth(message, progress),
					);
				if (!alive) return;
				setSession(newSession);
				const deepLinkContext = await getActivityDeepLinks(config);
				if (alive && deepLinkContext) {
					deepLinksRef.current = deepLinkContext;
					const deepLinks = deepLinkContext.deepLinks;
					const probeLink = deepLinks.createUrl("/link-check");
					console.info("[Library Tracking] Deeplink capability", {
						available: probeLink !== null,
						guildContextAvailable:
							deepLinkContext.guildContextAvailable,
						channelContextAvailable:
							deepLinkContext.channelContextAvailable,
					});
					setDirectLinksAvailable(probeLink !== null);
					const receiveDeepLink = (link: { hash: string }) => {
						const route = parseActivityRoute(link.hash);
						if (!route) {
							console.warn(
								"[Library Tracking] Ignored an incoming deeplink with an unsupported route.",
							);
							return;
						}
						console.info("[Library Tracking] Received a deeplink route", {
							hasTarget: route.target !== null,
						});
						setIncomingRoute({
							...route,
							sequence: ++routeSequenceRef.current,
						});
					};
					unsubscribeDeepLinks = deepLinks.subscribe(receiveDeepLink);
					const incoming = deepLinks.getSnapshot();
					if (incoming) receiveDeepLink(incoming);
				} else {
					console.info(
						"[Library Tracking] Deeplink capability check skipped because the Discord Activity SDK is not active.",
					);
				}
				if (!config.localDevActive && config.activityRequired)
					await setActivityPresence({
						activity: {
							type: 0,
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
				await loadNotions(false, await getIncomingCustomId(config));
			} catch (cause) {
				if (alive)
					setAuth(
						"Unable to authenticate this Discord Activity.",
						100,
						cause instanceof Error ? cause.message : String(cause),
						true,
					);
			}
		})();
		return () => {
			alive = false;
			unsubscribeDeepLinks?.();
			deepLinksRef.current = null;
		};
	}, []);

	useEffect(() => {
		if (
			!incomingRoute ||
			!notions.length ||
			incomingRoute.sequence <= appliedRouteSequenceRef.current
		)
			return;
		appliedRouteSequenceRef.current = incomingRoute.sequence;
		const notion = notions.find(
			(item) =>
				item.id.replaceAll("-", "").toLowerCase() ===
				incomingRoute.pageId,
		);
		if (!notion) return;
		setTargetLibrary(
			incomingRoute.target
				? {
						pageId: incomingRoute.pageId,
						name: incomingRoute.target,
					}
				: null,
		);
		void selectNotion(notion.id);
	}, [incomingRoute, notions]);

	useEffect(() => {
		if (!targetLibrary || !currentNotion) return;
		if (
			currentNotion.id.replaceAll("-", "").toLowerCase() !==
			targetLibrary.pageId
		)
			return;
		if (targetLibrary.name === "top") {
			mainContentRef.current?.scrollTo({ top: 0, behavior: "smooth" });
			setTargetLibrary(null);
			return;
		}
		const library = currentNotion.libraries.find(
			(item) => item.name.toLowerCase() === targetLibrary.name,
		);
		if (!library) {
			setTargetLibrary(null);
			return;
		}
		const targetId = `library-${encodeURIComponent(library.name.toLowerCase())}`;
		requestAnimationFrame(() => {
			document.getElementById(targetId)?.scrollIntoView({
				behavior: "smooth",
				block: "center",
			});
		});
		const timeout = window.setTimeout(() => setTargetLibrary(null), 2500);
		return () => window.clearTimeout(timeout);
	}, [currentNotion, targetLibrary]);

	async function provisionQuickLink(notionId: string) {
		const pending = quickLinkProvisioning.get(notionId);
		if (pending) return pending;
		const attempt = (async () => {
			try {
				await fetchJson<void>(
					`/api/tracking/notions/${encodeURIComponent(notionId)}/quick-link`,
					{ method: "POST" },
				);
			} catch {
				/* Best-effort preview provisioning; custom-id routing still works. */
			}
		})();
		quickLinkProvisioning.set(notionId, attempt);
		return attempt;
	}

	async function shareCurrentActivity() {
		if (!currentNotion) return;
		setShareBusy(true);
		try {
			await provisionQuickLink(currentNotion.id);
			const route = createActivityRoute(currentNotion.id, "top");
			const deepLinkUrl = deepLinksRef.current?.deepLinks.createUrl(route);
			const shareId =
				route.length <= 64 &&
				deepLinkUrl
					? route
					: currentNotion.customId;
			console.info("[Library Tracking] Sharing tracker page", {
				usingRoute: shareId === route,
				directLinkAvailable: deepLinkUrl !== null && deepLinkUrl !== undefined,
			});
			const result = await shareActivityLink(
				shareId,
				`Open ${currentNotion.title} in Library Tracking.`,
			);
			console.info("[Library Tracking] Tracker share dialog completed", {
				success: result.success,
				didCopyLink: result.didCopyLink,
				didSendMessage: result.didSendMessage,
			});
		} catch (cause) {
			showError(cause, "Couldn't share this Activity link");
		} finally {
			setShareBusy(false);
		}
	}

	async function shareLibraryLink(libraryName: string) {
		if (!currentNotion) return;
		const route = createActivityRoute(currentNotion.id, libraryName);
		const deepLinkUrl = deepLinksRef.current?.deepLinks.createUrl(route);
		if (
			route.length > 64 ||
			!deepLinkUrl
		)
			return;

		setShareBusy(true);
		try {
			const result = await shareActivityLink(
				route,
				`Open ${libraryName} in ${currentNotion.title} in Library Tracking.`,
			);
			console.info("[Library Tracking] Library share dialog completed", {
				success: result.success,
				didCopyLink: result.didCopyLink,
				didSendMessage: result.didSendMessage,
			});
		} catch (cause) {
			showError(cause, "Couldn't share this library link");
		} finally {
			setShareBusy(false);
		}
	}

	function openShareChartDialog(kind: "status" | "language") {
		setShareChartContent("");
		setShareChartDialogKind(kind);
	}

	async function shareChart() {
		const kind = shareChartDialogKind;
		if (!currentNotion || !kind) return;
		const content = shareChartContent.trim();
		const chart =
			kind === "status" ? statusChart.current : languageChart.current;
		const chartTitle =
			kind === "status" ? "Implementation status" : "Language support";
		setShareChartBusy(kind);
		try {
			const image = await createChartShareImage(
				currentNotion,
				chart,
				chartTitle,
			);
			setShareChartDialogKind(null);
			if (content) {
				await openActivityShareContent(
					await fileToDataUrl(image),
					image.name,
					content,
				);
			} else {
				const form = new FormData();
				form.append("image", image);
				const response = await fetchJson<ShareImageResponse>(
					`/api/tracking/notions/${encodeURIComponent(currentNotion.id)}/share-image`,
					{ method: "POST", body: form },
				);
				await openActivityShareMoment(response.mediaUrl);
			}
		} catch (cause) {
			showError(cause, "Couldn't share this chart image");
		} finally {
			setShareChartBusy(null);
		}
	}

	function openLibraryEditor(library: Library) {
		setEditLibrary(library);
		setEditStatus(library.status);
		setEditUrl(library.implementationUrl ?? "");
		setEditVersion(library.version ?? "");
		setEditNotes(library.notes ?? "");
	}
	async function submitLibraryEdit(event: React.FormEvent) {
		event.preventDefault();
		if (!editLibrary || !currentNotion) return;
		setEditLibraryBusy(true);
		try {
			await fetchJson<void>(
				`/api/tracking/notions/${encodeURIComponent(currentNotion.id)}/libraries/update`,
				{
					method: "POST",
					headers: { "Content-Type": "application/json" },
					body: JSON.stringify({
						libraryName: editLibrary.name,
						status: editStatus,
						prCommit: editUrl.trim() || null,
						version: editVersion.trim() || null,
						notes: editNotes.trim() || null,
					}),
				},
			);
			setEditLibrary(null);
			await loadNotions(true);
		} catch (cause) {
			showError(cause, "Couldn't update this library");
		} finally {
			setEditLibraryBusy(false);
		}
	}

	async function toggleNotion(notion: AdminNotion) {
		try {
			await fetchJson<void>(
				`/api/admin/notions/${encodeURIComponent(notion.id)}/${notion.enabled ? "disable" : "enable"}`,
				{ method: "POST" },
			);
			await Promise.all([loadNotions(true), loadAdminNotions()]);
			setAdminStatus(
				`${notion.name} ${notion.enabled ? "disabled" : "enabled"}.`,
			);
		} catch (cause) {
			showError(
				cause,
				`Couldn't ${notion.enabled ? "disable" : "enable"} this tracker`,
			);
		}
	}
	async function createNotion(event: React.FormEvent) {
		event.preventDefault();
		setCreateBusy(true);
		setAdminStatus("Creating the Notion page and applying its template…");
		try {
			const created = await fetchJson<NotionCreationResponse>(
				"/api/admin/notions",
				{
					method: "POST",
					headers: { "Content-Type": "application/json" },
					body: JSON.stringify({
						title: createTitle.trim(),
						description: createDescription.trim(),
						autoEnable: createAutoEnable,
					}),
				},
			);
			setCreateTitle("");
			setCreateDescription("");
			setCreateAutoEnable(true);
			await Promise.all([loadNotions(true), loadAdminNotions()]);
			setAdminStatus(
				`${created.name} created${created.enabled ? " and enabled" : " as disabled"}.${!created.titleUpdated || !created.descriptionUpdated ? " Check the title and description in Notion." : ""}`,
			);
		} catch (cause) {
			showError(cause, "Couldn't create a Notion tracker");
			setAdminStatus("Tracker creation failed. Check the error above.");
		} finally {
			setCreateBusy(false);
		}
	}
	async function openMetadataEditor(notion: AdminNotion) {
		try {
			const metadata = await fetchJson<NotionMetadata>(
				`/api/admin/notions/${encodeURIComponent(notion.id)}/metadata`,
			);
			setMetadataNotion(notion);
			setMetadataTitle(metadata.title);
			setMetadataDescription(metadata.description);
		} catch (cause) {
			showError(cause, "Couldn't load this tracker's details");
		}
	}
	async function submitMetadata(event: React.FormEvent) {
		event.preventDefault();
		if (!metadataNotion) return;
		setMetadataBusy(true);
		try {
			const result = await fetchJson<{
				titleUpdated: boolean;
				descriptionUpdated: boolean;
			}>(
				`/api/admin/notions/${encodeURIComponent(metadataNotion.id)}/metadata`,
				{
					method: "PUT",
					headers: { "Content-Type": "application/json" },
					body: JSON.stringify({
						title: metadataTitle.trim(),
						description: metadataDescription.trim(),
					}),
				},
			);
			if (result.titleUpdated && result.descriptionUpdated)
				setMetadataNotion(null);
			await Promise.all([loadNotions(true), loadAdminNotions()]);
			setAdminStatus(
				result.titleUpdated && result.descriptionUpdated
					? "Tracker title and description updated."
					: `Partially updated: title ${result.titleUpdated ? "saved" : "failed"}; description ${result.descriptionUpdated ? "saved" : "failed"}.`,
			);
		} catch (cause) {
			showError(cause, "Couldn't update this tracker");
		} finally {
			setMetadataBusy(false);
		}
	}

	const viewer = session?.user;
	const canEditLibrary = (library: Library) =>
		!!viewer &&
		["Admin", "Library Developer", "Employee"].includes(viewer.type) &&
		(viewer.type !== "Library Developer" ||
			!!viewer.libraries?.includes(library.name));
	const counts = new Map(
		(currentNotion?.statusCounts ?? []).map((item) => [
			item.name,
			item.count,
		]),
	);
	const total = (currentNotion?.statusCounts ?? []).reduce(
		(sum, item) => sum + item.count,
		0,
	);
	const visibleAdminNotions = adminNotions.filter(
		(notion) => !notion.isTemplate,
	);

	return (
		<div className="activity-shell">
			{!session ? (
				<section
					className={`auth-card ${authFailed ? "auth-card-error" : ""}`}
				>
					<div className="brand-mark">
						<img src="discord.png" alt="" />
					</div>
					<Heading level={1}>Library Development Tracking</Heading>
					<p>
						{authFailed
							? "Unable to authenticate this Discord Activity."
							: authMessage}
					</p>
					<ProgressBar
						value={authProgress}
						aria-label="Activity authentication"
					/>
					{authDetail && <p className="auth-detail">{authDetail}</p>}
				</section>
			) : (
				<>
					<aside className="sidebar">
						<header className="sidebar-header">
							<div className="brand-mark small">
								<img src="discord.png" alt="" />
							</div>
							<div>
								<strong>Library Tracking</strong>
								<span>Discord Libraries</span>
							</div>
						</header>
						<div className="sidebar-heading">
							<span>Notions</span>
							<div className="sidebar-tools">
								<Button
									text="Refresh"
									variant="secondary"
									size="sm"
									loading={refreshing}
									onClick={() => void loadNotions(true)}
								/>
								{canManageNotions && (
									<Button
										text="Manage"
										size="sm"
										variant="secondary"
										onClick={() => {
											setManageOpen(true);
											void loadAdminNotions();
										}}
									/>
								)}
							</div>
						</div>
						<nav
							className="notion-list"
							aria-label="Tracked notions"
						>
							{loadingNotions && !notions.length ? (
								<div className="loading-copy">
									Loading trackers…
								</div>
							) : notions.length ? (
								notions.map((notion) => (
									<button
										type="button"
										key={notion.id}
										className={`notion-item ${notion.id === selectedId ? "selected" : ""}`}
										aria-label={`Select ${notion.name}`}
										aria-current={
											notion.id === selectedId
												? "page"
												: undefined
										}
										onClick={() =>
											void selectNotion(notion.id)
										}
									>
										<span
											className="notion-icon"
											aria-hidden="true"
										>
											▤
										</span>
										<span>{notion.name}</span>
									</button>
								))
							) : (
								<p className="muted sidebar-empty">
									No tracked notions
								</p>
							)}
						</nav>
						<footer className="viewer-card">
							{viewer && <ViewerAvatar viewer={viewer} />}
							<div className="viewer-copy">
								<strong>
									{viewer?.displayName?.trim() ||
										viewer?.username}
								</strong>
								<span>
									{session.localDev
										? `Local development • @${viewer?.username}`
										: `@${viewer?.username}`}
								</span>
								<span>
									Access:{" "}
									{session.localDev
										? "Admin"
										: (viewer?.type ?? "External")}
								</span>
								<span>
									Libraries:{" "}
									{session.localDev ||
									viewer?.type === "Admin" ||
									viewer?.type === "Employee"
										? "All"
										: viewer?.libraries
												?.filter(
													(library) =>
														library &&
														library !== "undefined",
												)
												.join(", ") || "None"}
								</span>
							</div>
						</footer>
					</aside>
					<main className="main-content" ref={mainContentRef}>
						{error && (
							<div className="error-banner" role="alert">
								<div>
									<strong>{error.title}</strong>
									<span>{error.message}</span>
								</div>
								<button
									type="button"
									aria-label="Dismiss error"
									onClick={clearError}
								>
									×
								</button>
							</div>
						)}
						{loadingDetails && (
							<section
								className="dashboard-skeleton"
								role="status"
								aria-label="Loading tracker details"
							>
								<header className="dashboard-skeleton-header">
									<span className="skeleton-block skeleton-page-icon" />
									<div className="dashboard-skeleton-title">
										<span className="skeleton-block skeleton-eyebrow" />
										<span className="skeleton-block skeleton-title" />
										<span className="skeleton-block skeleton-description" />
									</div>
									<div className="dashboard-skeleton-actions">
										<span className="skeleton-block" />
										<span className="skeleton-block" />
									</div>
								</header>
								<div className="dashboard-skeleton-metrics">
									{Array.from({ length: 5 }, (_, index) => (
										<div className="dashboard-skeleton-metric" key={index}>
											<span className="skeleton-block" />
											<span className="skeleton-block" />
											<span className="skeleton-block" />
										</div>
									))}
								</div>
								<div className="dashboard-skeleton-charts">
									<div className="dashboard-skeleton-surface">
										<div className="dashboard-skeleton-surface-heading">
											<span className="skeleton-block" />
											<span className="skeleton-block" />
										</div>
										<div className="dashboard-skeleton-donut">
											<span className="skeleton-block" />
										</div>
									</div>
									<div className="dashboard-skeleton-surface">
										<div className="dashboard-skeleton-surface-heading">
											<span className="skeleton-block" />
											<span className="skeleton-block" />
										</div>
										<div className="dashboard-skeleton-bars">
											{[34, 56, 72, 48, 88, 62, 42, 80].map(
												(height, index) => (
													<span
														className="skeleton-block"
														key={index}
														style={{ height: `${height}%` }}
													/>
												),
											)}
										</div>
									</div>
								</div>
								<div className="dashboard-skeleton-surface dashboard-skeleton-table">
									<div className="dashboard-skeleton-surface-heading">
										<span className="skeleton-block" />
										<span className="skeleton-block" />
									</div>
									<div className="dashboard-skeleton-table-heading">
										{Array.from({ length: 5 }, (_, index) => (
											<span className="skeleton-block" key={index} />
										))}
									</div>
									{Array.from({ length: 5 }, (_, row) => (
										<div className="dashboard-skeleton-table-row" key={row}>
											{Array.from({ length: 5 }, (_, column) => (
												<span
													className="skeleton-block"
													key={column}
												/>
											))}
										</div>
									))}
								</div>
							</section>
						)}
						{!currentNotion && !loadingDetails && (
							<section className="welcome-panel">
								<span className="welcome-icon">▤</span>
								<Heading level={1}>
									{notions.length
										? "Select a tracked notion"
										: "No trackers available"}
								</Heading>
								<p>
									{notions.length
										? "Choose a notion from the sidebar to view its implementation statistics."
										: "No tracked notions are configured yet."}
								</p>
							</section>
						)}
						{currentNotion && (
							<>
								<header className="page-header">
									<div className="page-title">
										<span className="page-icon">
											{currentNotion.icon || "📄"}
										</span>
										<div>
											<span className="eyebrow">
												Implementation tracking
											</span>
											<Heading level={1}>
												{currentNotion.title}
											</Heading>
											<p>
												{currentNotion.description ||
													"No description is available for this notion."}
											</p>
										</div>
									</div>
									<div className="page-actions">
										<span className="sync-state">
											<i />
											{currentNotion.lastEditedAt
												? `Updated ${new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(new Date(currentNotion.lastEditedAt))}`
												: "Loaded from Notion"}
										</span>
										<Button
											text="Open in Notion"
											variant="secondary"
											disabled={!currentNotion.url}
											onClick={() =>
												currentNotion.url &&
												void openActivityLink(
													currentNotion.url,
												)
											}
										/>
										<Button
											text="Invite Page"
											variant="secondary"
											loading={shareBusy}
											disabled={!currentNotion.customId}
											onClick={() =>
												void shareCurrentActivity()
											}
										/>
									</div>
								</header>
								<section
									className="metrics-grid"
									aria-label="Implementation status"
								>
									{statuses.map((status) => (
										<article
											className="metric-card"
											key={status}
										>
											<span>{status}</span>
											<strong>
												{counts.get(status) ?? 0}
											</strong>
											<small>
												{total
													? `${Math.round(((counts.get(status) ?? 0) / total) * 100)}%`
													: "0%"}
											</small>
											<i
												style={{
													background:
														statusColors[status],
												}}
											/>
										</article>
									))}
								</section>
								<section className="chart-grid">
									<article className="surface chart-panel">
										<header className="surface-heading">
											<div>
												<Heading level={2}>
													Implementation status
												</Heading>
												<p>
													Current progress across
													tracked libraries
												</p>
											</div>
											<Button
												text="Share"
												size="sm"
												variant="secondary"
												loading={
													shareChartBusy === "status"
												}
												onClick={() =>
													openShareChartDialog("status")
												}
											/>
										</header>
										<div className="chart-canvas doughnut">
											<StatusChart
												kind="doughnut"
												notion={currentNotion}
												onReady={(chart) => {
													statusChart.current = chart;
												}}
											/>
										</div>
									</article>
									<article className="surface chart-panel">
										<header className="surface-heading">
											<div>
												<Heading level={2}>
													Language support
												</Heading>
												<p>
													Implementation status
													grouped by language
												</p>
											</div>
											<Button
												text="Share"
												size="sm"
												variant="secondary"
												loading={
													shareChartBusy ===
													"language"
												}
												onClick={() =>
													openShareChartDialog("language")
												}
											/>
										</header>
										<div className="chart-canvas">
											<StatusChart
												kind="bar"
												notion={currentNotion}
												onReady={(chart) => {
													languageChart.current =
														chart;
												}}
											/>
										</div>
									</article>
								</section>
								<section className="surface libraries-panel">
									<header className="surface-heading">
										<div>
											<Heading level={2}>
												Libraries
											</Heading>
											<p>
												Implementation state for this
												notion
											</p>
										</div>
										<Badge
											type={{
												text: `${currentNotion.libraries.length} ${currentNotion.libraries.length === 1 ? "library" : "libraries"}`,
											}}
										/>
									</header>
									<div className="table-wrap">
										<Table stickyHeader>
											<TableHeader>
												<TableRow>
													<TableColumn>
														Library
													</TableColumn>
													<TableColumn>
														Language
													</TableColumn>
													<TableColumn>
														Status
													</TableColumn>
													<TableColumn>
														Version
													</TableColumn>
													<TableColumn>
														Implementation
													</TableColumn>
													<TableColumn>
														Actions
													</TableColumn>
												</TableRow>
											</TableHeader>
											<TableBody>
												{currentNotion.libraries.map(
													(library) => (
														<TableRow
															key={library.name}
														>
															<TableCell>
																<div className="library-cell">
																	<button
																		id={`library-${encodeURIComponent(library.name.toLowerCase())}`}
																		className={`library-deep-link ${
																			targetLibrary?.pageId ===
																				currentNotion.id.replaceAll("-", "").toLowerCase() &&
																				targetLibrary.name === library.name.toLowerCase()
																			? "targeted"
																			: ""
																		}`}
																		type="button"
																		aria-label={`Share a link to ${library.name}`}
																		title={
														!directLinksAvailable
															? "Library links are available inside Discord app channels"
															: createActivityRoute(
																currentNotion.id,
																library.name,
															).length > 64
																? "This library name is too long for a direct share link"
																: `Share a link to ${library.name}`
																		}
																		disabled={
																			!directLinksAvailable ||
																			createActivityRoute(
																				currentNotion.id,
																				library.name,
																			).length > 64
																		}
																		onClick={() =>
																			void shareLibraryLink(library.name)
																		}
																	>
																		<strong>{library.name}</strong>
																	</button>
																	{library.notes && (
																		<LibraryNotes
																			name={
																				library.name
																			}
																			note={
																				library.notes
																			}
																		/>
																	)}
																</div>
															</TableCell>
															<TableCell>
																{
																	library.language
																}
															</TableCell>
															<TableCell>
																<Badge
																	type={{
																		text: library.status,
																	}}
																	variant={
																		library.status ===
																		"Released"
																			? "success"
																			: library.status ===
																				  "In Progress"
																				? "warning"
																				: "secondary"
																	}
																/>
															</TableCell>
															<TableCell>
																{library.version ||
																	"—"}
															</TableCell>
															<TableCell>
																{library.implementationUrl ? (
																	<Button
																		text="Open"
																		size="sm"
																		variant="secondary"
																		onClick={() =>
																			void openActivityLink(
																				library.implementationUrl!,
																			)
																		}
																	/>
																) : (
																	"—"
																)}
															</TableCell>
															<TableCell>
																{canEditLibrary(
																	library,
																) ? (
																	<Button
																		text="Edit"
																		size="sm"
																		onClick={() =>
																			openLibraryEditor(
																				library,
																			)
																		}
																	/>
																) : (
																	"—"
																)}
															</TableCell>
														</TableRow>
													),
												)}
											</TableBody>
										</Table>
									</div>
								</section>
							</>
						)}
					</main>
				</>
			)}

			<Modal
				open={shareChartDialogKind !== null}
				onClose={() => {
					if (shareChartBusy === null) setShareChartDialogKind(null);
				}}
				size="sm"
			>
				<ModalHeader
					title="Share chart"
					subtitle={`${shareChartDialogKind === "status" ? "Implementation status" : "Language support"} · ${currentNotion?.title ?? ""}`}
				/>
				<ModalBody>
					<TextArea
						label="Comment (optional)"
						description="Leave blank for the original share dialog, or add a message to send with the image."
						rows={4}
						maxLength={2000}
						showCharacterCount
						disabled={shareChartBusy !== null}
						value={shareChartContent}
						onChange={setShareChartContent}
						placeholder="Add a comment about this chart (optional)"
					/>
				</ModalBody>
				<ModalActionBar
					actions={[
						{
							text: "Cancel",
							variant: "secondary",
							disabled: shareChartBusy !== null,
							onClick: () => setShareChartDialogKind(null),
						},
						{
							text: "Share chart",
							variant: "primary",
							loading: shareChartBusy !== null,
							disabled: shareChartBusy !== null,
							onClick: () => void shareChart(),
						},
					]}
				/>
			</Modal>

			<Modal
				open={!!editLibrary}
				onClose={() => !editLibraryBusy && setEditLibrary(null)}
				size="md"
			>
				<ModalHeader
					title="Update library status"
					subtitle={editLibrary?.name}
				/>
				<ModalBody>
					{editLibrary && (
						<form
							id="library-edit-form"
							onSubmit={(event) => void submitLibraryEdit(event)}
							className="form-stack"
						>
							<Select<string>
								label="Status"
								required
								fullWidth
								value={editStatus}
								onChange={setEditStatus}
								options={statuses.map((status) => ({
									value: status,
									label: status,
								}))}
							/>
							<TextInput
								label="Pull request / commit"
								type="url"
								value={editUrl}
								onChange={setEditUrl}
							/>
							<TextInput
								label="Version"
								value={editVersion}
								onChange={setEditVersion}
							/>
							<TextArea
								label="Details"
								rows={4}
								value={editNotes}
								onChange={setEditNotes}
							/>
						</form>
					)}
				</ModalBody>
				<ModalActionBar
					actions={[
						{
							text: "Cancel",
							variant: "secondary",
							disabled: editLibraryBusy,
							onClick: () => setEditLibrary(null),
						},
						{
							text: "Update library",
							variant: "primary",
							loading: editLibraryBusy,
							onClick: () =>
								(
									document.getElementById(
										"library-edit-form",
									) as HTMLFormElement | null
								)?.requestSubmit(),
						},
					]}
				/>
			</Modal>

			<Modal
				open={manageOpen}
				onClose={() => setManageOpen(false)}
				size="lg"
				className="manage-modal"
			>
				<ModalHeader
					title="Manage Notion trackers"
					subtitle="Staff tools for the implementation dashboard"
				/>
				<ModalBody>
					<div className="manage-content">
						<section className="manage-create">
							<Heading level={3}>Create from template</Heading>
							<form
								id="create-notion-form"
								onSubmit={(event) => void createNotion(event)}
								className="form-stack"
							>
								<TextInput
									label="Title"
									maxLength={100}
									showCharacterCount
									value={createTitle}
									onChange={setCreateTitle}
									placeholder="e.g. Phase 5"
									required
								/>
								<TextArea
									label="Description"
									rows={3}
									maxLength={500}
									showCharacterCount
									value={createDescription}
									onChange={setCreateDescription}
									placeholder="What this tracker covers"
									required
								/>
								<Checkbox
									label="Enable this tracker for library commands"
									checked={createAutoEnable}
									onChange={setCreateAutoEnable}
								/>
								<Button
									text="Create tracker"
									loading={createBusy}
									type="submit"
								/>
							</form>
						</section>
						<section>
							<div className="admin-list-heading">
								<div>
									<Heading level={3}>Enabled</Heading>
									<p>
										Available to library tracking commands
									</p>
								</div>
								<Button
									text="Refresh"
									size="sm"
									variant="secondary"
									loading={adminLoading}
									onClick={() => void loadAdminNotions()}
								/>
							</div>
							<div className="admin-list">
								{visibleAdminNotions
									.filter((item) => item.enabled)
									.map((item) => (
										<AdminRow
											key={item.id}
											notion={item}
											onEdit={() =>
												void openMetadataEditor(item)
											}
											onToggle={() => toggleNotion(item)}
										/>
									))}
								{!visibleAdminNotions.some(
									(item) => item.enabled,
								) && (
									<p className="muted">
										No enabled trackers.
									</p>
								)}
							</div>
						</section>
						<section>
							<div className="admin-list-heading">
								<div>
									<Heading level={3}>
										Disabled and discoverable
									</Heading>
									<p>
										Known disabled trackers and matching
										Notion pages not enabled yet
									</p>
								</div>
							</div>
							<div className="admin-list">
								{visibleAdminNotions
									.filter((item) => !item.enabled)
									.map((item) => (
										<AdminRow
											key={item.id}
											notion={item}
											onEdit={() =>
												void openMetadataEditor(item)
											}
											onToggle={() => toggleNotion(item)}
										/>
									))}
								{!visibleAdminNotions.some(
									(item) => !item.enabled,
								) && (
									<p className="muted">
										No disabled or discoverable trackers.
									</p>
								)}
							</div>
						</section>
						{adminStatus && (
							<p
								className="admin-status"
								role="status"
								aria-live="polite"
							>
								{adminStatus}
							</p>
						)}
					</div>
				</ModalBody>
			</Modal>

			<Modal
				open={!!metadataNotion}
				onClose={() => !metadataBusy && setMetadataNotion(null)}
				size="md"
			>
				<ModalHeader
					title="Edit tracker"
					subtitle="Update the Notion title and description"
				/>
				<ModalBody>
					<form
						id="metadata-form"
						onSubmit={(event) => void submitMetadata(event)}
						className="form-stack"
					>
						<TextInput
							label="Title"
							maxLength={2000}
							value={metadataTitle}
							onChange={setMetadataTitle}
							required
						/>
						<TextArea
							label="Description"
							rows={5}
							maxLength={2000}
							value={metadataDescription}
							onChange={setMetadataDescription}
							required
						/>
					</form>
				</ModalBody>
				<ModalActionBar
					actions={[
						{
							text: "Cancel",
							variant: "secondary",
							disabled: metadataBusy,
							onClick: () => setMetadataNotion(null),
						},
						{
							text: "Save changes",
							variant: "primary",
							loading: metadataBusy,
							onClick: () =>
								(
									document.getElementById(
										"metadata-form",
									) as HTMLFormElement | null
								)?.requestSubmit(),
						},
					]}
				/>
			</Modal>
		</div>
	);
}

function ViewerAvatar({ viewer }: { viewer: ViewerIdentity }) {
	const [failed, setFailed] = useState(false);
	const name = viewer.displayName?.trim() || viewer.username;
	if (!viewer.avatarHash || failed)
		return (
			<div className="avatar avatar-fallback">
				{Array.from(name)[0]?.toUpperCase() || "?"}
			</div>
		);
	const extension = viewer.avatarHash.startsWith("a_") ? "gif" : "webp";
	return (
		<img
			className="avatar"
			src={`https://cdn.discordapp.com/avatars/${encodeURIComponent(viewer.id)}/${viewer.avatarHash}.${extension}?size=128`}
			alt={`${name}'s avatar`}
			onError={() => setFailed(true)}
		/>
	);
}

function LibraryNotes({ name, note }: { name: string; note: string }) {
	const triggerRef = useRef<HTMLButtonElement>(null);
	const [open, setOpen] = useState(false);
	return (
		<>
			<ManaTooltip text="View notes" position="top">
				<button
					ref={triggerRef}
					className="note-trigger"
					type="button"
					aria-label={`View notes for ${name}`}
					aria-expanded={open}
					onClick={() => setOpen((current) => !current)}
				>
					i
				</button>
			</ManaTooltip>
			<Popover
				targetElementRef={triggerRef}
				shouldShow={open}
				onRequestClose={() => setOpen(false)}
				position="top"
				align="start"
				title={`Notes · ${name}`}
				body={<span className="note-popover-body">{note}</span>}
				aria-label={`Notes for ${name}`}
			/>
		</>
	);
}

function AdminRow({
	notion,
	onEdit,
	onToggle,
}: {
	notion: AdminNotion;
	onEdit: () => void;
	onToggle: () => Promise<void>;
}) {
	const [busy, setBusy] = useState(false);
	return (
		<div className="admin-row">
			<div className="admin-row-copy">
				<strong>{notion.name}</strong>
				<small>{notion.id}</small>
			</div>
			<div className="admin-row-actions">
				<Button
					text="Edit"
					variant="secondary"
					size="sm"
					onClick={onEdit}
				/>
				<Button
					text={notion.enabled ? "Disable" : "Enable"}
					variant={notion.enabled ? "secondary" : "primary"}
					size="sm"
					loading={busy}
					onClick={async () => {
						setBusy(true);
						try {
							await onToggle();
						} finally {
							setBusy(false);
						}
					}}
				/>
			</div>
		</div>
	);
}

async function fileToDataUrl(file: File) {
	return new Promise<string>((resolve, reject) => {
		const reader = new FileReader();
		reader.onload = () => {
			if (typeof reader.result === "string") resolve(reader.result);
			else reject(new Error("The chart image could not be read."));
		};
		reader.onerror = () =>
			reject(reader.error ?? new Error("The chart image could not be read."));
		reader.readAsDataURL(file);
	});
}

async function createChartShareImage(
	notion: NotionDetails,
	chart: Chart | null,
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
	const chartTop = Math.max(descriptionBottom + 92, 390),
		chartHeight = canvas.height - chartTop - 90,
		chartLeft = 90,
		chartWidth = canvas.width - chartLeft * 2;
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
		),
		width = Math.round(image.width * scale),
		height = Math.round(image.height * scale);
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
	let line = "",
		lineCount = 0;
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
function truncateText(value: string, maximumLength: number) {
	return value.length <= maximumLength
		? value
		: `${value.slice(0, Math.max(1, maximumLength - 1)).trimEnd()}…`;
}

const root = document.getElementById("root");
if (!root) throw new Error("Missing React root element.");
createRoot(root).render(<App />);
