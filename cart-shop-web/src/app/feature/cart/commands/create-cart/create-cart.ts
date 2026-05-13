import { Component, inject, output, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';

// Mirrors CartShop.Core/Feature/Cart/Commands/CreateCart/Handler.cs
interface CreateCartRequest { customerName: string; }
interface CreateCartResponse { cartId: string; }

@Component({
  selector: 'cs-create-cart',
  imports: [FormsModule],
  template: `
    <section class="card">
      <h2>Start a new cart</h2>
      <div class="row">
        <input
          [ngModel]="customerName()"
          (ngModelChange)="customerName.set($event)"
          placeholder="Customer name"
          (keyup.enter)="submit()" />
        <button (click)="submit()" [disabled]="busy()">Create cart</button>
      </div>
      @if (error()) { <p class="error-inline">{{ error() }}</p> }
    </section>
  `,
})
export class CreateCart {
  private http = inject(HttpClient);

  customerName = signal('');
  busy = signal(false);
  error = signal<string | null>(null);

  cartCreated = output<string>();

  async submit() {
    const name = this.customerName().trim();
    if (!name) { this.error.set('Enter a customer name first'); return; }
    this.error.set(null);
    this.busy.set(true);
    try {
      const body: CreateCartRequest = { customerName: name };
      const res = await this.http.post<CreateCartResponse>('/api/carts', body).toPromise();
      this.customerName.set('');
      this.cartCreated.emit(res!.cartId);
    } catch (e: any) {
      this.error.set(e?.error?.error ?? e?.message ?? 'Failed');
    } finally {
      this.busy.set(false);
    }
  }
}
