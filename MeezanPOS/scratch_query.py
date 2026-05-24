import sqlite3

def main():
    conn = sqlite3.connect(r'D:\Meezan sys\MeezanPOS\Meezan.db')
    cursor = conn.cursor()
    
    print("--- Bank Transactions of Type 5 (SupplierPayment) ---")
    cursor.execute("SELECT Id, Type, Amount, ReferenceNumber, Notes, SourceType, SourceId FROM BankTransactions WHERE Type = 5")
    rows = cursor.fetchall()
    for row in rows:
        print(row)
        
    print("\n--- Suppliers in Database ---")
    cursor.execute("SELECT Id, Name FROM Suppliers")
    suppliers = cursor.fetchall()
    for s in suppliers:
        print(s)
        
    conn.close()

if __name__ == '__main__':
    main()
