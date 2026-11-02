// 相机标定：角点检测 + 内参/畸变标定
#include "va_internal.h"
#include "va_error.h"

namespace va {

int CalibFindChessboard(Context* ctx, const VA_Image* gray, int cols, int rows,
                        double* outXY, int cap, int* outCount)
{
    if (gray == 0 || outXY == 0 || outCount == 0 || cap <= 0 || cols < 2 || rows < 2)
    {
        SetError(ctx, "null argument or bad grid size");
        return VA_ERR_ARG;
    }

    *outCount = 0;

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;

        cv::Mat work;
        if (g.channels() == 3) cv::cvtColor(g, work, cv::COLOR_BGR2GRAY);
        else if (g.channels() == 4) cv::cvtColor(g, work, cv::COLOR_BGRA2GRAY);
        else work = g;

        std::vector<cv::Point2f> pts;
        const int flags = cv::CALIB_CB_ADAPTIVE_THRESH | cv::CALIB_CB_NORMALIZE_IMAGE
                        | cv::CALIB_CB_FAST_CHECK;

        bool ok = cv::findChessboardCorners(work, cv::Size(cols, rows), pts, flags);
        if (!ok)
        {
            // 兜底再试一次：去掉 FAST_CHECK（有些低对比度图第一遍会漏）
            ok = cv::findChessboardCorners(work, cv::Size(cols, rows), pts,
                                           cv::CALIB_CB_ADAPTIVE_THRESH | cv::CALIB_CB_NORMALIZE_IMAGE);
        }

        if (!ok || (int)pts.size() != cols * rows)
        {
            SetError(ctx, "未检测到棋盘格角点（检查内角点行列数、对比度与完整成像）");
            return VA_ERR_NO_EDGE;
        }

        // 亚像素精化（标定精度几乎全靠这一步）
        cv::cornerSubPix(work, pts, cv::Size(11, 11), cv::Size(-1, -1),
                         cv::TermCriteria(cv::TermCriteria::EPS + cv::TermCriteria::COUNT, 30, 0.01));

        int n = (int)pts.size();
        if (n > cap) n = cap;
        for (int i = 0; i < n; ++i)
        {
            outXY[i * 2 + 0] = pts[i].x;
            outXY[i * 2 + 1] = pts[i].y;
        }

        *outCount = n;
        return VA_OK;
    }
    catch (const cv::Exception& e)
    {
        SetError(ctx, e.what());
        return VA_ERR_INTERNAL;
    }
}

int CalibCalibrate(Context* ctx, const double* xy, const int* counts, int nImages,
                   int cols, int rows, int width, int height, int flags,
                   double* outK, double* outDist, double* outRms)
{
    if (xy == 0 || counts == 0 || outK == 0 || outDist == 0 || outRms == 0
        || nImages < 3 || width <= 0 || height <= 0 || cols < 2 || rows < 2)
    {
        SetError(ctx, "需要至少 3 张有效图，且棋盘格行列数 >= 2");
        return VA_ERR_ARG;
    }

    try
    {
        std::vector<std::vector<cv::Point3f> > objectPoints;
        std::vector<std::vector<cv::Point2f> > imagePoints;

        // 每张图的点必须一样多，否则视为无效图（跳过）
        int expect = counts[0];
        if (expect < 4) { SetError(ctx, "too few points per image"); return VA_ERR_ARG; }

        // 棋盘格内角点的世界坐标：Z=0 平面，单位用"格"。
        // 必须按 cols/rows 排成真正的网格 —— 一行排到底会破坏靶标几何，标出来的 K 是错的。
        if (expect != cols * rows)
        {
            SetError(ctx, "每张图的点数与 cols*rows 不一致");
            return VA_ERR_ARG;
        }

        std::vector<cv::Point3f> board;
        board.reserve((size_t)expect);
        for (int r = 0; r < rows; ++r)
            for (int c = 0; c < cols; ++c)
                board.push_back(cv::Point3f((float)c, (float)r, 0.0f));

        int offset = 0;
        for (int i = 0; i < nImages; ++i)
        {
            int cnt = counts[i];
            if (cnt != expect) { offset += cnt * 2; continue; }

            std::vector<cv::Point2f> img;
            img.reserve((size_t)cnt);
            for (int k = 0; k < cnt; ++k)
            {
                img.push_back(cv::Point2f((float)xy[(offset + k) * 2 + 0],
                                          (float)xy[(offset + k) * 2 + 1]));
            }
            offset += cnt * 2;

            objectPoints.push_back(board);
            imagePoints.push_back(img);
        }

        if (objectPoints.size() < 3)
        {
            SetError(ctx, "有效标定图不足 3 张（角点检测失败的图会被跳过）");
            return VA_ERR_ARG;
        }

        cv::Mat K, dist;
        std::vector<cv::Mat> rvecs, tvecs;

        // 先用标准标志试；解出 NaN 就退一步：固定 k3 并置零切向畸变（自由度少、更稳），
        // 很多"图少 / 姿态接近"的现场都是靠这一步拿到可用结果的。
        double rms = cv::calibrateCamera(objectPoints, imagePoints, cv::Size(width, height),
                                         K, dist, rvecs, tvecs, flags);

        if (!(rms == rms))
        {
            K = cv::Mat();
            dist = cv::Mat();
            rvecs.clear();
            tvecs.clear();

            rms = cv::calibrateCamera(objectPoints, imagePoints, cv::Size(width, height),
                                      K, dist, rvecs, tvecs,
                                      flags | cv::CALIB_FIX_K3 | cv::CALIB_ZERO_TANGENT_DIST);
        }

        if (K.rows != 3 || K.cols != 3 || dist.empty())
        {
            SetError(ctx, "calibrateCamera 返回空结果");
            return VA_ERR_INTERNAL;
        }

        // 退化场景（所有图姿态几乎相同 / 纯仿射）会解出 NaN，别让它当成成功返回
        if (!(rms == rms) || rms < 0)          // NaN 检查：NaN != NaN
        {
            char dbg[512];
            std::snprintf(dbg, sizeof(dbg),
                "rms=%g fx=%g fy=%g cx=%g cy=%g d0=%g d1=%g d2=%g d3=%g d4=%g imgs=%d pts=%d",
                rms, K.at<double>(0, 0), K.at<double>(1, 1), K.at<double>(0, 2), K.at<double>(1, 2),
                (int)dist.total() > 0 ? dist.at<double>(0) : 0.0,
                (int)dist.total() > 1 ? dist.at<double>(1) : 0.0,
                (int)dist.total() > 2 ? dist.at<double>(2) : 0.0,
                (int)dist.total() > 3 ? dist.at<double>(3) : 0.0,
                (int)dist.total() > 4 ? dist.at<double>(4) : 0.0,
                (int)objectPoints.size(), expect);
            SetError(ctx, dbg);
            return VA_ERR_INTERNAL;
        }
        for (int i = 0; i < 9; ++i)
        {
            double v = K.at<double>(i / 3, i % 3);
            if (!(v == v)) { SetError(ctx, "内参出现 NaN，标定图姿态不足"); return VA_ERR_INTERNAL; }
        }

        for (int r = 0; r < 3; ++r)
            for (int c = 0; c < 3; ++c)
                outK[r * 3 + c] = K.at<double>(r, c);

        int dn = (int)dist.total() * (int)dist.channels();
        for (int i = 0; i < 5; ++i)
            outDist[i] = (i < dn) ? dist.at<double>(i) : 0.0;

        *outRms = rms;
        return VA_OK;
    }
    catch (const cv::Exception& e)
    {
        SetError(ctx, e.what());
        return VA_ERR_INTERNAL;
    }
}

} // namespace va
