import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  EMPTY,
  Subscription,
  catchError,
  exhaustMap,
  finalize,
  forkJoin,
  interval,
  switchMap,
  filter,
  tap,
} from 'rxjs';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { Participant } from '@app/models/group-orders/participant.model';
import { GroupStatusResponse } from '@app/models/group-orders/group-status.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { consolidatePreOrder } from '@app/shared/helpers/consolidate-pre-order';
import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';
import { ModalComponent } from '@app/shared/custom-components/modal/modal.component';
@Component({
  selector: 'app-group-order-page',
  standalone: true,
  imports: [CurrencyPipe, DatePipe, ParticipantCardComponent, ModalComponent],
  templateUrl: './group-order-page.component.html',
  styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GroupOrderPageComponent implements OnInit {
  private readonly service = inject(GroupOrdersService);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private timer?: Subscription;
  private refreshRequest?: Subscription;
  private loadRequest?: Subscription;
  private reservationId = '';
  readonly invite = signal<GuestInvite | null>(null);
  readonly participants = signal<Participant[]>([]);
  readonly ownerSessionId = signal('');
  readonly status = signal<GroupStatusResponse['status']>('Open');
  readonly loading = signal(true);
  readonly error = signal('');
  readonly submitting = signal(false);
  readonly removing = signal(false);
  readonly refreshedAt = signal<Date | null>(null);
  readonly refreshError = signal(false);
  readonly refreshing = signal(false);
  readonly confirmSubmit = signal(false);
  readonly removeTarget = signal<Participant | null>(null);
  readonly consolidated = computed(() => consolidatePreOrder(this.participants()));
  readonly pendingCount = computed(
    () => this.participants().filter((person) => !person.isSubmitted).length
  );
  readonly empty = computed(() => !this.participants().some((person) => person.items.length));
  readonly readOnly = computed(() => this.status() !== 'Open');
  readonly canSubmit = computed(
    () =>
      !this.readOnly() &&
      !this.submitting() &&
      !this.removing() &&
      this.consolidated().items.length > 0
  );
  ngOnInit(): void {
    this.reservationId = this.route.snapshot.paramMap.get('reservationId') ?? '';
    this.load();
  }
  load(): void {
    this.timer?.unsubscribe();
    this.refreshRequest?.unsubscribe();
    this.loadRequest?.unsubscribe();
    this.refreshing.set(false);
    this.loading.set(true);
    this.error.set('');
    this.loadRequest = this.service
      .getGroupInvite$(this.reservationId)
      .pipe(
        switchMap((invite) =>
          forkJoin({
            sessions: this.service.getSessions$(invite.GroupReservationId),
            owner: this.service.getOwnerSessionId$(this.reservationId),
            status: this.service.getStatus$(invite.GroupReservationId),
          }).pipe(
            tap((result) => {
              this.invite.set(invite);
              this.participants.set(result.sessions.participants);
              this.ownerSessionId.set(result.owner);
              this.status.set(result.status.status);
              this.refreshedAt.set(new Date());
              this.refreshError.set(false);
            })
          )
        ),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false))
      )
      .subscribe({
        next: () => this.startSync(),
        error: (error: HttpErrorResponse) =>
          this.error.set(
            error.status === 404
              ? 'Group reservation not found'
              : 'Unable to load group pre-order. Please try again.'
          ),
      });
  }
  private startSync(): void {
    this.timer?.unsubscribe();
    if (this.readOnly()) return;
    this.timer = interval(15_000)
      .pipe(
        filter(
          () => !this.readOnly() && !this.submitting() && !this.removing() && !this.refreshing()
        ),
        exhaustMap(() => this.fetchSnapshot()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }
  private fetchSnapshot() {
    const groupId = this.invite()?.GroupReservationId;
    if (!groupId || this.readOnly() || this.submitting() || this.removing() || this.refreshing())
      return EMPTY;
    this.refreshing.set(true);
    return forkJoin({
      sessions: this.service.getSessions$(groupId, { silent: true }),
      status: this.service.getStatus$(groupId, { silent: true }),
    }).pipe(
      tap((result) => {
        this.participants.set(result.sessions.participants);
        this.status.set(result.status.status);
        this.refreshedAt.set(new Date());
        this.refreshError.set(false);
        if (this.readOnly()) this.timer?.unsubscribe();
      }),
      catchError(() => {
        this.refreshError.set(true);
        return EMPTY;
      }),
      finalize(() => this.refreshing.set(false))
    );
  }
  refresh(): void {
    if (!this.refreshing())
      this.refreshRequest = this.fetchSnapshot()
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe();
  }
  submit(): void {
    const groupId = this.invite()?.GroupReservationId;
    if (!groupId || !this.canSubmit()) return;
    this.timer?.unsubscribe();
    this.refreshRequest?.unsubscribe();
    this.submitting.set(true);
    this.service
      .submit$(groupId, this.reservationId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false))
      )
      .subscribe({
        next: () => {
          this.status.set('Submitted');
          this.timer?.unsubscribe();
          this.confirmSubmit.set(false);
          this.toast.success('Pre-order sent to the restaurant');
        },
        error: () => {
          this.confirmSubmit.set(false);
          this.startSync();
        },
      });
  }
  removeGuest(): void {
    const guest = this.removeTarget();
    const groupId = this.invite()?.GroupReservationId;
    if (!guest || !groupId || this.removing() || this.submitting() || this.readOnly()) return;
    this.timer?.unsubscribe();
    this.refreshRequest?.unsubscribe();
    this.removing.set(true);
    this.service
      .removeSession$(groupId, guest.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.removing.set(false))
      )
      .subscribe({
        next: (response) => {
          this.participants.set(response.participants);
          this.refreshedAt.set(new Date());
          this.removeTarget.set(null);
          this.startSync();
        },
        error: () => {
          this.removeTarget.set(null);
          this.startSync();
        },
      });
  }
}
