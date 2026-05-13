import { Component, inject, output, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';

// Mirrors CartShop.Core/Feature/Inventory/Commands/SetStock/Handler.cs
interface SetStockRequest { quantity: number; }

@Component({
  selector: 'cs-set-stock',
  imports: [FormsModule],
  template: `
    <section class="card">
      <h2>Set stock</h2>
      <p class="hint">Establishes inventory for a SKU. DCB consistency kicks in once stock is set.</p>
      <div class="row item-row">
        <input [ngModel]="sku()" (ngModelChange)="sku.set($event)" placeholder="SKU" />
        <input type="number" min="0" [ngModel]="quantity()" (ngModelChange)="quantity.set(+$event)" placeholder="Stock qty" />
        <button (click)="submit()" [disabled]="busy()">Set stock</button>
      </div>
      @if (error()) { <p class="error-inline">{{ error() }}</p> }
    </section>
  `,
})
export class SetStock {
  private http = inject(HttpClient);

  sku = signal('');
  quantity = signal(0);
  busy = signal(false);
  error = signal<string | null>(null);

  stockSet = output<string>();

  async submit() {
    const skuVal = this.sku().trim();
    if (!skuVal) { this.error.set('SKU is required'); return; }
    this.error.set(null);
    this.busy.set(true);
    try {
      const body: SetStockRequest = { quantity: Number(this.quantity()) || 0 };
      await this.http.post(`/api/inventory/${encodeURIComponent(skuVal)}`, body).toPromise();
      this.stockSet.emit(skuVal);
    } catch (e: any) {
      this.error.set(e?.error?.error ?? e?.message ?? 'Failed');
    } finally {
      this.busy.set(false);
    }
  }
}
