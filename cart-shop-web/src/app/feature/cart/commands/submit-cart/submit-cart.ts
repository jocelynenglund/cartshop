import { Component, inject, input, output, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';

// Mirrors CartShop.Core/Feature/Cart/Commands/SubmitCart/Handler.cs
interface SubmitCartResponse { cartId: string; totalAmount: number; submittedAt: string; }

@Component({
  selector: 'cs-submit-cart',
  template: `
    <button class="primary" (click)="submit()" [disabled]="busy() || disabled()">
      Submit cart
    </button>
    @if (error()) { <p class="error-inline">{{ error() }}</p> }
  `,
})
export class SubmitCart {
  private http = inject(HttpClient);

  cartId = input.required<string>();
  disabled = input(false);

  busy = signal(false);
  error = signal<string | null>(null);

  cartSubmitted = output<SubmitCartResponse>();

  async submit() {
    this.busy.set(true);
    this.error.set(null);
    try {
      const res = await this.http
        .post<SubmitCartResponse>(`/api/carts/${this.cartId()}/submit`, {})
        .toPromise();
      this.cartSubmitted.emit(res!);
    } catch (e: any) {
      this.error.set(e?.error?.error ?? e?.message ?? 'Failed');
    } finally {
      this.busy.set(false);
    }
  }
}
