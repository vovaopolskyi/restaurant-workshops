import { TestBed } from '@angular/core/testing';
import { ParticipantCardComponent } from './participant-card.component';

describe('Participant card presentation before interactions', () => {
  it('renders the quantity symbol and card border on its initial render', () => {
    const fixture = TestBed.createComponent(ParticipantCardComponent);
    fixture.componentRef.setInput('participant', {
      id: 'guest', type: 'Guest', name: 'Alex', total: 20, isSubmitted: false,
      items: [{ dish: { id: 'salad', name: 'Salad', price: 10 }, quantity: 2 }],
    });
    fixture.detectChanges();
    const article = fixture.nativeElement.querySelector('article') as HTMLElement;
    expect(article.classList.contains('participant-card')).toBeTrue();
    expect(article.querySelector('.quantity')?.textContent?.trim()).toBe('\u00d7 2');
    expect(getComputedStyle(article).borderTopWidth).toBe('1px');
    expect(getComputedStyle(article).backgroundColor).toBe('rgb(255, 255, 255)');
  });
});
