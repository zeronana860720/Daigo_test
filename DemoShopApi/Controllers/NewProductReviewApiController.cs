using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DemoShopApi.Models;
using DemoShopApi.DTOs;

namespace DemoShopApi.Controllers
{
    [ApiController]
    [Route("api/review/products")]
    [Tags("5 NewProductReviewApi")]

    public class NewProductReviewApiController : ControllerBase
    {
        private readonly StoreDbContext _db;

        public NewProductReviewApiController(StoreDbContext db)
        {
            _db = db;
        }
   
        private string GetCurrentReviewerUid()
        {
            var reviewerUid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(reviewerUid))
            {
                // 這裡你也可以選擇 return null，看你想怎麼處理
                throw new UnauthorizedAccessException("找不到使用者 Uid，請確認已登入並帶入 JWT。");
            }

            return reviewerUid;
        }
        // done
        [HttpGet("newpending")]  //  取得「第二波待審核商品清單」
        public async Task<IActionResult> GetPendingNewProducts()
        {
            var products = await _db.StoreProducts
                .Include(p => p.Store)
                .Where(p =>
                    p.Status == 1 &&           // 商品待審核
                    p.Store.Status == 3        // 賣場已發布
                )
                .Select(p => new
                   {
                    p.ProductId,
                    p.ProductName,
                    p.Price,
                    p.Quantity,
                    p.Description,
                    p.ImagePath,

                    StoreId = p.Store.StoreId,
                    StoreName = p.Store.StoreName,

                    p.CreatedAt,
                    SubmittedAt = p.CreatedAt
                })
                .ToListAsync();

            return Ok(products);
        }

        // done
        [HttpPost("{productId}/approveproduct")] // 審核通過 -> 商品發布  
        public async Task<IActionResult> ApproveProduct(int productId)

        {
            var reviewerUid = GetCurrentReviewerUid();
            
            // 檢查有沒有登入用的
            if (string.IsNullOrEmpty(reviewerUid))
                return Unauthorized("請先登入");
            var product = await _db.StoreProducts
            .Include(p => p.Store)
            .FirstOrDefaultAsync(p => p.ProductId == productId);
            
            if (product == null) 
                return NotFound("商品不存在");

            // 只能審核待審核商品
            if (product.Status != 1) // 待審核
                return BadRequest("此商品不在審核中");

            // // 賣場必須是已發布狀態
            // if (product.Store.Status != 3)
            //     return BadRequest("賣場未發布，無法審核商品");


            product.Status = 3;
            // 不用isActive了
            // product.IsActive = true;
            product.UpdatedAt = DateTime.Now;
            product.RejectReason = null;

            // 寫入商品審核紀錄
            _db.StoreProductReviews.Add(new StoreProductReview
            {
                ProductId = productId,
                // ReviewerUid = dto.ReviewerUid,
                // 改成從token抓
                ReviewerUid = reviewerUid,
                Result = 1,               // 通過
                CreatedAt = DateTime.Now
            });

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "商品審核通過，已成功上架"
            });
        }

        
        // 拒絕通過商品
        [HttpPost("{productId}/rejectproduct")]
        public async Task<IActionResult> RejectProduct(int productId, [FromBody] ReviewDto dto)
        {
            var reviewerUid = GetCurrentReviewerUid();
    
            if (string.IsNullOrEmpty(reviewerUid))
                return Unauthorized("請先登入");

            var product = await _db.StoreProducts
                .Include(p => p.Store)
                .FirstOrDefaultAsync(p => p.ProductId == productId);

            if (product == null)
                return NotFound("商品不存在");

            if (product.Status != 1)
                return BadRequest("此商品不在審核中");

            // 賣場停權不可操作
            if (product.Store.Status == 4)
                return BadRequest("賣場已停權，無法審核商品");

            // 商品退回
            product.Status = 2; // 審核失敗
            product.IsActive = false; // 前端不顯示
            product.RejectReason = dto.Comment;
            product.UpdatedAt = DateTime.Now;

            // 寫入商品審核紀錄
            _db.StoreProductReviews.Add(new StoreProductReview
            {
                ProductId = productId,
                ReviewerUid = reviewerUid,  // ← 從 Token 取得
                Result = 2,
                Comment = dto.Comment,
                CreatedAt = DateTime.Now
            });

            await _db.SaveChangesAsync();

            return Ok(new { message = "商品審核未通過，已退回賣家修改" });
        }

    }
}
