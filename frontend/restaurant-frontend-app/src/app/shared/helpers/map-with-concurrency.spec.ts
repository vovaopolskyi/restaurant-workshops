import { Observable, Subject } from 'rxjs';
import { mapWithConcurrency } from './map-with-concurrency';

describe('mapWithConcurrency', () => {
  it('limits concurrent requests and preserves order when responses arrive out of order', () => {
    const responses = [new Subject<string>(), new Subject<string>(), new Subject<string>()];
    const started: number[] = [];
    let result: string[] | undefined;
    let active = 0;
    let peak = 0;
    mapWithConcurrency([0, 1, 2], index => new Observable<string>(subscriber => {
      started.push(index);
      peak = Math.max(peak, ++active);
      const subscription = responses[index].subscribe(subscriber);
      return () => { active--; subscription.unsubscribe(); };
    }), 2).subscribe(value => result = value);
    expect(started).toEqual([0, 1]);
    responses[1].next('second');
    responses[1].complete();
    expect(started).toEqual([0, 1, 2]);
    responses[2].next('third');
    responses[2].complete();
    expect(result).toBeUndefined();
    responses[0].next('first');
    responses[0].complete();
    expect(peak).toBe(2);
    expect(active).toBe(0);
    expect(result).toEqual(['first', 'second', 'third']);
  });

  it('returns an empty list without issuing requests for empty input', () => {
    const project = jasmine.createSpy('project');
    let result: unknown;
    mapWithConcurrency([], project, 2).subscribe(value => result = value);
    expect(project).not.toHaveBeenCalled();
    expect(result).toEqual([]);
  });
});
