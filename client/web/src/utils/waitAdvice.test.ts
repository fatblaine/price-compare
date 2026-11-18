import type { UpcomingSpecial } from "../api/upcoming";
import {
	favoriteUpcomingParts,
	favoriteUpcomingText,
	formatStartDate,
	isComparable,
	isMeaningfulDrop,
	upcomingLine,
} from "./waitAdvice";

const next = (
	price: number,
	overrides: Partial<UpcomingSpecial> = {},
): UpcomingSpecial => ({
	shopType: 1,
	price,
	priceUnit: "each",
	wasPrice: null,
	saveAmount: null,
	promoText: null,
	unitPriceText: null,
	validFrom: "2026-11-18",
	validTo: "2026-11-24",
	...overrides,
});

const shopName = (t: number) => (t === 0 ? "Coles" : "Woolworths");

describe("isMeaningfulDrop", () => {
	it("needs at least 10c and 3%", () => {
		expect(isMeaningfulDrop(5.5, 2.75)).toBe(true);
		expect(isMeaningfulDrop(5.0, 4.9)).toBe(false); // 10c but only 2%
		expect(isMeaningfulDrop(2.0, 1.95)).toBe(false); // 5c
		expect(isMeaningfulDrop(20.0, 19.5)).toBe(false); // 50c but 2.5%
		expect(isMeaningfulDrop(3.0, 2.9)).toBe(true); // 10c, 3.3%
		expect(isMeaningfulDrop(3.0, 3.5)).toBe(false); // dearer
	});
});

describe("isComparable", () => {
	it("rejects multi-buy and per-kg prices", () => {
		expect(isComparable(next(3, { priceUnit: "multibuy" }))).toBe(false);
		expect(isComparable(next(3, { priceUnit: "kg" }))).toBe(false);
		expect(isComparable(next(3, { priceUnit: "kg*" }))).toBe(false);
		expect(isComparable(next(3, { priceUnit: "pk" }))).toBe(true);
		expect(isComparable(next(3, { priceUnit: null }))).toBe(true);
		expect(isComparable(null)).toBe(false);
	});
});

describe("formatStartDate", () => {
	it("formats the calendar date without a time-zone shift", () => {
		expect(formatStartDate("2026-11-18")).toBe("Wed 18 Nov");
		expect(formatStartDate("2027-01-06")).toBe("Wed 6 Jan");
	});
});

describe("upcomingLine", () => {
	it("shows this store's lower price from Wednesday", () => {
		const line = upcomingLine({
			herePrice: 5.5,
			hereShopType: 1,
			hereNext: next(2.75),
			shopName,
		});
		expect(line?.text).toBe("From Wed 18 Nov: $2.75");
		expect(line?.shortText).toBe("Wed: $2.75");
	});

	it("says nothing when the product is not in next week's catalogue", () => {
		expect(
			upcomingLine({
				herePrice: 5.5,
				hereShopType: 1,
				hereNext: null,
				shopName,
			}),
		).toBeNull();
	});

	it("says nothing when next week is not meaningfully cheaper", () => {
		expect(
			upcomingLine({
				herePrice: 3.0,
				hereShopType: 1,
				hereNext: next(2.95),
				shopName,
			}),
		).toBeNull();
		expect(
			upcomingLine({
				herePrice: 3.0,
				hereShopType: 1,
				hereNext: next(3.5),
				shopName,
			}),
		).toBeNull();
	});

	it("points to the other store when its next-week price is the lowest", () => {
		const line = upcomingLine({
			herePrice: 6.0,
			hereShopType: 1,
			hereNext: null,
			otherPrice: 5.0,
			otherShopType: 0,
			otherNext: next(2.5, { shopType: 0 }),
			shopName,
		});
		expect(line?.text).toBe("Wed 18 Nov: $2.50 at Coles");
		expect(line?.shortText).toBe("Wed: $2.50 Coles");
	});

	it("compares against the cheaper of today's two prices", () => {
		// Woolworths drops to $4.50 next week, but Coles already sells it for $4.00 today.
		const line = upcomingLine({
			herePrice: 6.0,
			hereShopType: 1,
			hereNext: next(4.5),
			otherPrice: 4.0,
			otherShopType: 0,
			otherNext: null,
			shopName,
		});
		expect(line).toBeNull();
	});

	it("ignores multi-buy offers", () => {
		const line = upcomingLine({
			herePrice: 9.0,
			hereShopType: 1,
			hereNext: next(7.5, {
				priceUnit: "multibuy",
				promoText: "Any 2 for $15.00",
			}),
			shopName,
		});
		expect(line).toBeNull();
	});

	it("prefers this store on a tie", () => {
		const line = upcomingLine({
			herePrice: 6.0,
			hereShopType: 1,
			hereNext: next(3.0),
			otherPrice: 6.0,
			otherShopType: 0,
			otherNext: next(3.0, { shopType: 0 }),
			shopName,
		});
		expect(line?.text).toBe("From Wed 18 Nov: $3.00");
	});
});

describe("favoriteUpcomingText", () => {
	it("shows next week's special with the catalogue's own saving", () => {
		expect(favoriteUpcomingText(next(2.75, { saveAmount: 2.75 }))).toBe(
			"On special from Wed 18 Nov: $2.75 (save $2.75)",
		);
	});

	it("stays quiet without a saving or for multi-buys", () => {
		expect(
			favoriteUpcomingText(
				next(3.5, { saveAmount: null, promoText: "Every Day" }),
			),
		).toBeNull();
		expect(
			favoriteUpcomingText(next(7.5, { priceUnit: "multibuy", saveAmount: 1 })),
		).toBeNull();
		expect(favoriteUpcomingText(null)).toBeNull();
	});
});

describe("favoriteUpcomingParts", () => {
	it("splits the favourites line into date, price and saving", () => {
		expect(favoriteUpcomingParts(next(2.75, { saveAmount: 2.75 }))).toEqual({
			startDate: "Wed 18 Nov",
			price: "$2.75",
			save: "$2.75",
		});
	});

	it("follows the same rule as favoriteUpcomingText", () => {
		expect(favoriteUpcomingParts(next(3.5, { saveAmount: 0.05 }))).toBeNull();
		expect(
			favoriteUpcomingParts(next(7.5, { priceUnit: "multibuy", saveAmount: 1 })),
		).toBeNull();
		expect(favoriteUpcomingParts(undefined)).toBeNull();
	});
});
