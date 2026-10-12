import type { ProductCard } from '../types';

/** At most one card in four of a grid shows an ad cover (G-VIS round 5, L203). */
export const COVER_SHARE = 0.25;

/**
 * The cards of a grid with their photos mixed like a real marketplace feed (G-VIS round 5, L203): an ad cover (a photo
 * with text printed on it) stays only while covers are at most a quarter of the cards placed so far and the card before
 * it is not a cover; otherwise the card shows its plain photo. Decided in order from the first card, so loading the next
 * page never changes a card already on screen. A cover without a plain photo stays (there is nothing else to show).
 */
export const mixCovers = (cards: ProductCard[]): ProductCard[] => {
  let covers = 0;
  let previousCover = false;
  return cards.map((card, i) => {
    if (!card.imageHasText) {
      previousCover = false;
      return card;
    }
    const allowed = !previousCover && covers + 1 <= (i + 1) * COVER_SHARE;
    if (allowed || !card.photoUrl) {
      covers++;
      previousCover = true;
      return card;
    }
    previousCover = false;
    return { ...card, imageUrl: card.photoUrl, imageHasText: false };
  });
};
