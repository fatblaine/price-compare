import type { UpcomingSpecial } from "../api/upcoming";

/**
 * BTS-163: when to tell a shopper that a product gets cheaper from Wednesday.
 * Only certain information is used: a product that is not in next week's catalogue has an
 * unknown next-week price, never "the same price" and never "dearer".
 */

/** Drops smaller than 10c or 3% are not worth waiting two days for. */
export const MIN_DROP_ABS = 0.1;
export const MIN_DROP_PCT = 0.03;

export function isMeaningfulDrop(now: number, next: number): boolean {
	if (!Number.isFinite(now) || !Number.isFinite(next) || now <= 0) return false;
	const drop = now - next;
	return drop >= MIN_DROP_ABS - 1e-9 && drop / now >= MIN_DROP_PCT - 1e-9;
}

/**
 * Multi-buy prices ("2 for $15", stored per unit) and per-kg prices cannot be compared with a
 * single-item shelf price.
 */
export function isComparable(
	upcoming: UpcomingSpecial | null | undefined,
): upcoming is UpcomingSpecial {
	if (!upcoming || !Number.isFinite(upcoming.price) || upcoming.price <= 0)
		return false;
	const unit = (upcoming.priceUnit ?? "").trim().toLowerCase();
	return unit !== "multibuy" && !unit.startsWith("kg");
}

const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const MONTHS = [
	"Jan",
	"Feb",
	"Mar",
	"Apr",
	"May",
	"Jun",
	"Jul",
	"Aug",
	"Sep",
	"Oct",
	"Nov",
	"Dec",
];

/** "2026-11-18" -> "Wed 18 Nov". Parsed as a calendar date, so the viewer's time zone cannot shift it. */
export function formatStartDate(isoDate: string): string {
	const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(isoDate ?? "");
	if (!m) return "Wed";
	const year = Number(m[1]);
	const month = Number(m[2]) - 1;
	const day = Number(m[3]);
	const weekday = new Date(Date.UTC(year, month, day)).getUTCDay();
	return `${WEEKDAYS[weekday]} ${day} ${MONTHS[month]}`;
}

const money = (v: number) => `$${v.toFixed(2)}`;

export interface UpcomingLine {
	text: string;
	/** Phone-width variant: keeps the price visible on a two-column card. */
	shortText: string;
	shopType: number;
	price: number;
}

/**
 * The single "next week" line for a product card, or null.
 * - Considers this product's next-week price and, when given, the same product at the other store.
 * - Shown only when the cheapest known next-week price beats the cheapest price today by a
 *   meaningful amount.
 */
export function upcomingLine(input: {
	herePrice: number | null | undefined;
	hereShopType: number;
	hereNext: UpcomingSpecial | null | undefined;
	otherPrice?: number | null;
	otherShopType?: number | null;
	otherNext?: UpcomingSpecial | null;
	shopName: (shopType: number) => string;
}): UpcomingLine | null {
	const {
		herePrice,
		hereShopType,
		hereNext,
		otherPrice,
		otherShopType,
		otherNext,
		shopName,
	} = input;
	if (herePrice == null || !Number.isFinite(herePrice)) return null;

	const nowPrices = [herePrice];
	if (otherPrice != null && Number.isFinite(otherPrice))
		nowPrices.push(otherPrice);
	const bestNow = Math.min(...nowPrices);

	const candidates: { shopType: number; next: UpcomingSpecial }[] = [];
	if (isComparable(hereNext))
		candidates.push({ shopType: hereShopType, next: hereNext });
	if (otherShopType != null && otherPrice != null && isComparable(otherNext)) {
		candidates.push({ shopType: otherShopType, next: otherNext });
	}
	if (candidates.length === 0) return null;

	// Cheapest next-week offer; on a tie prefer this card's own store.
	const best = candidates.reduce((a, b) =>
		b.next.price < a.next.price ? b : a,
	);
	if (!isMeaningfulDrop(bestNow, best.next.price)) return null;

	const when = formatStartDate(best.next.validFrom);
	const weekday = when.split(" ")[0];
	const ownStore = best.shopType === hereShopType;
	const text = ownStore
		? `From ${when}: ${money(best.next.price)}`
		: `${when}: ${money(best.next.price)} at ${shopName(best.shopType)}`;
	const shortText = ownStore
		? `${weekday}: ${money(best.next.price)}`
		: `${weekday}: ${money(best.next.price)} ${shopName(best.shopType)}`;
	return { text, shortText, shopType: best.shopType, price: best.next.price };
}

/**
 * Favourites list line. The list has no current price, so it relies only on the catalogue's own
 * saving ("save $2.75"): shown when next week's offer is a real special, never as a price guess.
 */
export function favoriteUpcomingText(
	upcoming: UpcomingSpecial | null | undefined,
): string | null {
	const parts = favoriteUpcomingParts(upcoming);
	if (!parts) return null;
	return `On special from ${parts.startDate}: ${parts.price} (save ${parts.save})`;
}

/** The same rule as favoriteUpcomingText, split into pieces for a compact pill + saving label. */
export function favoriteUpcomingParts(
	upcoming: UpcomingSpecial | null | undefined,
): { startDate: string; price: string; save: string } | null {
	if (!isComparable(upcoming)) return null;
	const save = upcoming.saveAmount ?? 0;
	if (!(save >= MIN_DROP_ABS)) return null;
	return {
		startDate: formatStartDate(upcoming.validFrom),
		price: money(upcoming.price),
		save: money(save),
	};
}
