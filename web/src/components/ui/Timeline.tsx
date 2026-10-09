import type { ReactNode } from 'react';
import { Check } from 'lucide-react';
import './Timeline.css';

export type TimelineState = 'done' | 'current' | 'upcoming';

export interface TimelineItem {
  key: string;
  title: ReactNode;
  time?: ReactNode;
  description?: ReactNode;
  state: TimelineState;
  /** 'danger' paints a terminal step (cancelled, failed delivery) in the danger tone */
  tone?: 'default' | 'danger';
}

interface TimelineProps {
  items: TimelineItem[];
  /** vertical = history list (newest first is up to the caller); horizontal = stepper of a main flow */
  orientation?: 'vertical' | 'horizontal';
  label?: string;
  testId?: string;
  className?: string;
}

/** Dots joined by a line: done steps filled, the current one highlighted, upcoming ones greyed (G3). */
export const Timeline = ({ items, orientation = 'vertical', label, testId, className }: TimelineProps) => (
  <ol className={['sh-timeline', `sh-timeline--${orientation}`, className].filter(Boolean).join(' ')} aria-label={label} data-testid={testId}>
    {items.map((item) => (
      <li key={item.key} className={`sh-timeline__item is-${item.state}${item.tone === 'danger' ? ' is-danger' : ''}`}
        aria-current={item.state === 'current' ? 'step' : undefined}>
        <span className="sh-timeline__dot" aria-hidden>
          {orientation === 'horizontal' && item.state !== 'upcoming' && <Check size={16} strokeWidth={3} />}
        </span>
        <div className="sh-timeline__body">
          <span className="sh-timeline__title">{item.title}</span>
          {item.description && <span className="sh-timeline__desc">{item.description}</span>}
          {item.time && <span className="sh-timeline__time">{item.time}</span>}
        </div>
      </li>
    ))}
  </ol>
);

export default Timeline;
