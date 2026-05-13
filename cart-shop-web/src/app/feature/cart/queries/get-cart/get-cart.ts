import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { RemoveItem } from '../../commands/remove-item/remove-item';
import { AddItem } from '../../commands/add-item/add-item';
import { SubmitCart } from '../../commands/submit-cart/submit-cart';

// Mirrors CartShop.Core/Feature/Cart/Queries/GetCart/Handler.cs (CartAggregate shape)
export interface CartLine {
  itemId: string;
  sku: string;
  displayName: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface Cart {
  id: string;
  customerName: string;
  status: 'Open' | 'Submitted' | number;
  lines: CartLine[];
  createdAt: string;
  submittedAt?: string;
  total: number;
}

@Component({
  selector: 'cs-get-cart',
  imports: [CommonModule, RemoveItem, AddItem, SubmitCart],
  template: `
    @if (cart(); as c) {
      <section class="card">
        <header class="cart-head">
          <div>
            <h2>{{ c.customerName }}'s cart</h2>
            <small>{{ c.id }}</small>
          </div>
          <span class="status" [class.submitted]="!isOpen()">
            {{ isOpen() ? 'Open' : 'Submitted' }}
          </span>
        </header>

        @if (c.lines.length === 0) {
          <p class="empty">No items yet.</p>
        } @else {
          <table>
            <thead>
              <tr>
                <th>SKU</th><th>Name</th>
                <th class="num">Qty</th><th class="num">Price</th><th class="num">Line</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              @for (line of c.lines; track line.itemId) {
                <tr>
                  <td><code>{{ line.sku }}</code></td>
                  <td>{{ line.displayName }}</td>
                  <td class="num">{{ line.quantity }}</td>
                  <td class="num">{{ line.unitPrice | number:'1.2-2' }}</td>
                  <td class="num">{{ line.lineTotal | number:'1.2-2' }}</td>
                  <td>
                    @if (isOpen()) {
                      <cs-remove-item
                        [cartId]="c.id"
                        [itemId]="line.itemId"
                        (itemRemoved)="refresh()" />
                    }
                  </td>
                </tr>
              }
            </tbody>
            <tfoot>
              <tr>
                <td colspan="4" class="num"><strong>Total</strong></td>
                <td class="num"><strong>{{ c.total | number:'1.2-2' }}</strong></td>
                <td></td>
              </tr>
            </tfoot>
          </table>
        }

        @if (isOpen()) {
          <cs-add-item [cartId]="c.id" (itemAdded)="refresh()" />
          <div class="actions">
            <cs-submit-cart
              [cartId]="c.id"
              [disabled]="c.lines.length === 0"
              (cartSubmitted)="onSubmitted()" />
            <button class="ghost" (click)="discarded.emit()">Discard</button>
          </div>
        } @else {
          <div class="actions">
            <button class="primary" (click)="discarded.emit()">Start another cart</button>
          </div>
        }
      </section>
    }
  `,
})
export class GetCart {
  private http = inject(HttpClient);

  cartId = input.required<string>();

  cart = signal<Cart | null>(null);
  isOpen = computed(() => {
    const c = this.cart();
    if (!c) return false;
    return c.status === 'Open' || c.status === 0;
  });

  discarded = output<void>();
  cartSubmitted = output<void>();
  cartChanged = output<void>();

  constructor() {
    effect(() => {
      const id = this.cartId();
      if (id) this.load(id);
    });
  }

  refresh() {
    this.load(this.cartId());
    this.cartChanged.emit();
  }

  onSubmitted() {
    this.refresh();
    this.cartSubmitted.emit();
  }

  private async load(id: string) {
    try {
      const c = await this.http.get<Cart>(`/api/carts/${id}`).toPromise();
      this.cart.set(c ?? null);
    } catch {
      this.cart.set(null);
    }
  }
}
