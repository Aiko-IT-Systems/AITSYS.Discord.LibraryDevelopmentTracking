import { Common, DiscordSDK } from "@discord/embedded-app-sdk";

/**
 * Client-side Discord Activity authentication and session helpers.
 *
 * The backend owns authorization, session cookies, and OAuth token refreshes.
 * This module only coordinates the embedded Discord SDK with those backend
 * endpoints and exposes shared authenticated request and external-link helpers.
 */
const OAUTH_SCOPES = [Common.ScopesObject.identify, Common.ScopesObject.guilds, Common.ScopesObject["rpc.activities.write"]];

let cachedDiscordSdk: DiscordSDK | null = null;

let cachedAccessToken: {
	accessToken: string;
	expiresAtMs: number;
} | null = null;

/**
 * Runtime config returned by the backend before the embedded auth flow starts.
 */
export type AuthConfigResponse = {
	appId: string;
	activityRequired: boolean;
	localDevAllowed: boolean;
	localDevActive: boolean;
};

/**
 * Snapshot describing why the current viewer is authorized to use the Activity.
 */
export type AuthorizationSnapshot = {
	isAuthorized: boolean;
	viaTeam: boolean;
	viaUserAllowlist: boolean;
	viaGuildAllowlist: boolean;
};

/**
 * Minimal viewer profile attached to an authenticated Activity session.
 */
export type ViewerIdentity = {
	id: string;
	username: string;
	displayName?: string | null;
	avatarHash?: string | null;
	type: "External" | "Library Developer" | "Bot Developer" | "Employee" | "Admin"
	libraries?: string[];
};

/**
 * Authenticated session payload returned by the backend.
 */
export type SessionResponse = {
	user: ViewerIdentity;
	authorization: AuthorizationSnapshot;
	localDev: boolean;
	activeGuildId?: string | null;
	activeChannelId?: string | null;
};

type AuthExchangeResponse = {
	session: SessionResponse;
};

type AccessTokenResponse = {
	accessToken: string;
	expiresAt: string;
};

/**
 * Small HTTP error wrapper used so callers can branch on status codes cleanly.
 */
export class HttpError extends Error {
	public readonly status: number;

	public constructor(status: number, message: string) {
		super(message);
		this.name = "HttpError";
		this.status = status;
	}
}

/**
 * Fetches Activity bootstrap configuration from the backend.
 */
export async function getAuthConfig() {
	return fetchJson<AuthConfigResponse>("/api/auth/config");
}

/**
 * Returns the current authenticated session, or <c>null</c> when the viewer is
 * not signed in yet.
 */
export async function getSession() {
	try {
		return await fetchJson<SessionResponse>(buildSessionUrl());
	} catch (error) {
		if (error instanceof HttpError && error.status === 401) {
			return null;
		}

		throw error;
	}
}

/**
 * Logs out the current Activity session and clears the cached OAuth token.
 */
export async function logout() {
	setCachedAccessToken(null);
	await fetchJson("/api/auth/logout", { method: "POST" });
}

/**
 * Releases Activity orientation locks after the embedded SDK is ready.
 */
export async function enforceOrientation() {
	const discordSdk = await getDiscordSdk((await getAuthConfig()).appId);
	await discordSdk.commands.setOrientationLockState({
		lock_state: Common.OrientationLockStateTypeObject.UNLOCKED,
		picture_in_picture_lock_state:
			Common.OrientationLockStateTypeObject.UNLOCKED,
		grid_lock_state: Common.OrientationLockStateTypeObject.UNLOCKED,
	});
}

/**
 * Ensures the Activity has an authenticated backend session and Discord SDK
 * auth context.
 */
export async function ensureActivitySession(
	onProgress: (message: string, value: number) => void,
) {
	const config = await getAuthConfig();
	if (config.localDevActive) {
		const session = await getSession();
		if (!session) {
			throw new Error(
				"Local development is enabled, but the backend did not provide a local session.",
			);
		}

		return { config, session };
	}

	onProgress("Waiting for Discord Activity SDK.", 20);
	const discordSdk = await getDiscordSdk(config.appId);
	const existing = await getSession();
	if (existing) {
		try {
			onProgress("Authenticating Discord client.", 68);
			await authenticateDiscordClient(discordSdk);
			return { config, session: existing };
		} catch (error) {
			if (!(error instanceof HttpError) || error.status !== 401) {
				throw error;
			}

			setCachedAccessToken(null);
			onProgress("Re-establishing Discord authorization.", 58);
		}
	}

	await authorizeWithDiscord(config.appId, discordSdk, onProgress);

	onProgress("Finalizing Discord session.", 82);
	const session = await getSession();
	if (!session) {
		throw new Error(
			"Discord authentication succeeded, but the session cookie was not established.",
		);
	}

	onProgress("Authenticating Discord client.", 90);
	await authenticateDiscordClient(discordSdk, true);

	return { config, session };
}

/**
 * Opens an external link through Discord when embedded, or in a new browser
 * tab during local development.
 */
export async function openActivityLink(url: string) {
	const config = await getAuthConfig();
	if (config.localDevActive || !config.activityRequired) {
		window.open(url, "_blank", "noopener,noreferrer");
		return;
	}

	const discordSdk = await getDiscordSdk(config.appId);
	await discordSdk.commands.openExternalLink({ url });
}

/**
 * Opens Discord's native share-moment dialog for an Activity attachment URL.
 */
export async function openActivityShareMoment(mediaUrl: string) {
	const config = await getAuthConfig();
	if (config.localDevActive || !config.activityRequired) {
		throw new Error(
			"Chart sharing is only available inside the Discord Activity.",
		);
	}

	const discordSdk = await getDiscordSdk(config.appId);
	await discordSdk.commands.openShareMomentDialog({ mediaUrl });
}

/**
 * Opens Discord's native Activity-link dialog. The custom ID is deliberately
 * stable so recipients launch straight into the same configured notion.
 */
export async function shareActivityLink(customId: string, message: string) {
	const config = await getAuthConfig();
	if (config.localDevActive || !config.activityRequired) {
		throw new Error("Activity links can only be shared inside Discord.");
	}

	const discordSdk = await getDiscordSdk(config.appId);
	return discordSdk.commands.shareLink({
		message,
		custom_id: customId,
	});
}

/**
 * Sets the activity presence for the Discord client.
 * @param activity The activity object containing presence information.
 */
export async function setActivityPresence(activity: {
	activity: {
		name?: string;
		applicationId?: string;
		details?: string;
		state?: string;
		assets?: {
			large_image?: string;
			large_text?: string;
			small_image?: string;
			small_text?: string;
		};
		emoji: {
			name: string;
			id?: string;
		}
	};
}) {
	const config = await getAuthConfig();
	if (config.localDevActive || !config.activityRequired) {
		throw new Error(
			"Activity presence can only be set inside the Discord Activity.",
		);
	}

	const discordSdk = await getDiscordSdk(config.appId);
	await discordSdk.commands.setActivity(activity);
}

/**
 * Reads a custom Activity link identifier from the Discord SDK, with a query
 * string fallback for local routing and Discord client compatibility.
 */
export async function getIncomingCustomId(config?: AuthConfigResponse) {
	const resolvedConfig = config ?? (await getAuthConfig());
	const fromQuery = new URLSearchParams(window.location.search).get(
		"custom_id",
	);
	if (resolvedConfig.localDevActive || !resolvedConfig.activityRequired) {
		return fromQuery;
	}

	const discordSdk = await getDiscordSdk(resolvedConfig.appId);
	return discordSdk.customId || fromQuery;
}

/**
 * Fetches JSON with session cookies and normalized error messages.
 */
export async function fetchJson<T>(
	input: RequestInfo | URL,
	init?: RequestInit,
) {
	const response = await fetch(input, {
		credentials: "include",
		...init,
	});
	if (!response.ok) {
		let message = `${response.status} ${response.statusText}`;

		try {
			const payload = (await response.json()) as {
				message?: string;
				title?: string;
				detail?: string;
				errors?: Record<string, string[]>;
			};
			if (payload?.message) {
				message = payload.message;
			} else if (payload?.detail) {
				message = payload.detail;
			} else if (payload?.title) {
				message = payload.title;
			} else if (payload?.errors) {
				const validationMessage = Object.entries(payload.errors)
					.flatMap(([key, values]) =>
						values.map((value) => `${key}: ${value}`),
					)
					.join(" | ");
				if (validationMessage) {
					message = validationMessage;
				}
			}
		} catch {
			// The response did not contain a JSON problem payload.
		}

		throw new HttpError(response.status, message);
	}

	if (response.status === 204) {
		return undefined as T;
	}

	return response.json() as Promise<T>;
}

function buildSessionUrl() {
	const url = new URL("/api/auth/session", window.location.origin);
	const instanceId = getActivityInstanceId();
	const guildId = getActivityGuildId();
	const channelId = getActivityChannelId();
	if (instanceId) {
		url.searchParams.set("instance_id", instanceId);
	}

	if (guildId) {
		url.searchParams.set("guild_id", guildId);
	}

	if (channelId) {
		url.searchParams.set("channel_id", channelId);
	}

	return `${url.pathname}${url.search}`;
}

async function getDiscordSdk(appId: string) {
	if (!cachedDiscordSdk) {
		cachedDiscordSdk = new DiscordSDK(appId);
	}

	await cachedDiscordSdk.ready();
	return cachedDiscordSdk;
}

async function authorizeWithDiscord(
	appId: string,
	discordSdk: DiscordSDK,
	onProgress?: (message: string, value: number) => void,
) {
	onProgress?.("Authorizing inside Discord.", 45);
	let authorizeResult;
	try {
		authorizeResult = await requestAuthorizationCode(
			appId,
			discordSdk,
			"none",
		);
	} catch {
		onProgress?.("Discord requested consent.", 52);
		authorizeResult = await requestAuthorizationCode(
			appId,
			discordSdk,
			"consent",
		);
	}

	onProgress?.("Exchanging code with the backend.", 70);
	const instanceId = getActivityInstanceId();
	const guildId = getActivityGuildId();
	const channelId = getActivityChannelId();
	return fetchJson<AuthExchangeResponse>("/api/auth/exchange", {
		method: "POST",
		headers: { "Content-Type": "application/json" },
		body: JSON.stringify({
			code: authorizeResult.code,
			instanceId,
			guildId,
			channelId,
		}),
	});
}

function getActivityInstanceId() {
	return new URLSearchParams(window.location.search).get("instance_id");
}

function getActivityGuildId() {
	return new URLSearchParams(window.location.search).get("guild_id");
}

function getActivityChannelId() {
	return new URLSearchParams(window.location.search).get("channel_id");
}

function setCachedAccessToken(
	accessToken: string | null,
	expiresAt?: string | Date,
) {
	if (!accessToken) {
		cachedAccessToken = null;
		return;
	}

	const expiresAtMs =
		expiresAt instanceof Date
			? expiresAt.getTime()
			: typeof expiresAt === "string"
				? Date.parse(expiresAt)
				: Date.now() + 5 * 60 * 1000;
	cachedAccessToken = {
		accessToken,
		expiresAtMs: Number.isFinite(expiresAtMs)
			? expiresAtMs
			: Date.now() + 5 * 60 * 1000,
	};
}

async function getActivityAccessToken(forceRefresh = false) {
	if (
		!forceRefresh &&
		cachedAccessToken &&
		cachedAccessToken.expiresAtMs > Date.now() + 30_000
	) {
		return cachedAccessToken.accessToken;
	}

	const config = await getAuthConfig();
	if (config.localDevActive || !config.activityRequired) {
		throw new Error(
			"Discord access tokens are unavailable outside the Activity.",
		);
	}

	const tokenResponse = await fetchJson<AccessTokenResponse>(
		`/api/auth/access-token${forceRefresh ? "?forceRefresh=true" : ""}`,
		{ method: "POST" },
	);
	setCachedAccessToken(tokenResponse.accessToken, tokenResponse.expiresAt);
	return tokenResponse.accessToken;
}

async function authenticateDiscordClient(
	discordSdk: DiscordSDK,
	forceRefresh = false,
) {
	const accessToken = await getActivityAccessToken(forceRefresh);
	await discordSdk.commands.authenticate({
		access_token: accessToken,
	});
}

async function requestAuthorizationCode(
	appId: string,
	discordSdk: DiscordSDK,
	prompt: "none" | "consent",
) {
	return discordSdk.commands.authorize({
		client_id: appId,
		response_type: "code",
		scope: [...OAUTH_SCOPES],
		// The SDK accepts these supported prompt values, although its published type omits them.
		// @ts-ignore
		prompt,
		state: crypto.randomUUID(),
	});
}
